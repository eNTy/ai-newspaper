using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace NewspaperOrchestrator.Activities;

public class InstagramPublisherActivity
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _accessToken;
    private readonly string _apiVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InstagramPublisherActivity(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        _accessToken = Environment.GetEnvironmentVariable("INSTAGRAM_ACCESS_TOKEN")
            ?? throw new InvalidOperationException("INSTAGRAM_ACCESS_TOKEN environment variable is not set");

        _apiVersion = Environment.GetEnvironmentVariable("INSTAGRAM_API_VERSION") ?? "v24.0";
    }

    [Function(nameof(PublishToInstagram))]
    public async Task<InstagramPublishResult> PublishToInstagram(
        [ActivityTrigger] BatchResult batchResult,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(PublishToInstagram));
        logger.LogInformation("Publishing to Instagram for age group: {age}", batchResult.AudienceAge);

        // Extract video URLs
        var videoUrls = batchResult.Articles
            .Where(a => !string.IsNullOrEmpty(a.VideoUrl))
            .Select(a => a.VideoUrl)
            .ToList();

        if (videoUrls.Count == 0)
        {
            throw new InvalidOperationException("No video URLs found in batch result articles.");
        }

        logger.LogInformation("Found {count} videos to publish", videoUrls.Count);

        // Resolve Instagram account for this age group
        var accountId = GetAccountIdForAge(batchResult.AudienceAge);
        logger.LogInformation("Using Instagram account {accountId} for age group {age}", accountId, batchResult.AudienceAge);

        // Generate caption
        var caption = GenerateCaption(batchResult.Articles);
        logger.LogInformation("Generated caption: {caption}", caption);

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(20);

        // Create child video containers
        logger.LogInformation("Creating child video containers...");
        var childContainerIds = new List<string>();
        for (int i = 0; i < videoUrls.Count; i++)
        {
            var containerId = await CreateChildVideoContainerAsync(httpClient, accountId, videoUrls[i], logger);
            childContainerIds.Add(containerId);
            logger.LogInformation("Created child container {id} for video {index}", containerId, i + 1);
        }

        // Wait for all child containers to finish processing
        logger.LogInformation("Waiting for all child video containers to process...");
        for (int i = 0; i < childContainerIds.Count; i++)
        {
            var success = await WaitForProcessingAsync(httpClient, childContainerIds[i], $"video {i + 1}", logger);
            if (!success)
            {
                throw new InvalidOperationException($"Video {i + 1} container processing failed (container: {childContainerIds[i]})");
            }
        }

        // Create carousel container
        logger.LogInformation("Creating carousel container...");
        var carouselContainerId = await CreateCarouselContainerAsync(httpClient, accountId, childContainerIds, caption, logger);
        logger.LogInformation("Created carousel container: {id}", carouselContainerId);

        // Wait for carousel to be ready
        logger.LogInformation("Waiting for carousel to be ready...");
        var carouselReady = await WaitForProcessingAsync(httpClient, carouselContainerId, "carousel", logger);
        if (!carouselReady)
        {
            throw new InvalidOperationException($"Carousel container processing failed (container: {carouselContainerId})");
        }

        // Publish the carousel
        logger.LogInformation("Publishing carousel...");
        var mediaId = await PublishMediaAsync(httpClient, accountId, carouselContainerId, logger);
        logger.LogInformation("Successfully published carousel! Media ID: {mediaId}", mediaId);

        // Fetch the permalink
        var permalink = await GetPermalinkAsync(httpClient, mediaId, logger);

        return new InstagramPublishResult
        {
            MediaId = mediaId,
            Permalink = permalink
        };
    }

    private static string GetAccountIdForAge(int audienceAge)
    {
        var envVar = $"INSTAGRAM_ACCOUNT_ID_{audienceAge}";
        return Environment.GetEnvironmentVariable(envVar)
            ?? throw new InvalidOperationException(
                $"Environment variable {envVar} is not set. Each age group requires its own Instagram account ID.");
    }

    private static string GenerateCaption(List<ProcessedArticle> articles)
    {
        var titles = articles
            .Where(a => !string.IsNullOrEmpty(a.Title))
            .Select(a => a.Title)
            .ToList();

        var caption = string.Join("\n", titles);
        caption += "\n\n#zpravy #novinky #ainewspaper";

        return caption;
    }

    private async Task<string> CreateChildVideoContainerAsync(HttpClient httpClient, string accountId, string videoUrl, ILogger logger)
    {
        var encodedVideoUrl = Uri.EscapeDataString(videoUrl);
        var encodedToken = Uri.EscapeDataString(_accessToken);
        var url = $"https://graph.facebook.com/{_apiVersion}/{accountId}/media" +
                  $"?media_type=VIDEO&video_url={encodedVideoUrl}&is_carousel_item=true&access_token={encodedToken}";

        var response = await httpClient.PostAsync(url, null);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to create child video container. Status: {response.StatusCode}, Response: {responseBody}");
        }

        var result = JsonSerializer.Deserialize<GraphApiIdResponse>(responseBody, JsonOptions);
        if (result == null || string.IsNullOrEmpty(result.Id))
        {
            throw new InvalidOperationException($"No container ID returned. Response: {responseBody}");
        }

        return result.Id;
    }

    private async Task<string> GetContainerStatusAsync(HttpClient httpClient, string containerId)
    {
        var encodedToken = Uri.EscapeDataString(_accessToken);
        var url = $"https://graph.facebook.com/{_apiVersion}/{containerId}" +
                  $"?fields=status_code,status&access_token={encodedToken}";

        var response = await httpClient.GetAsync(url);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to get container status. Status: {response.StatusCode}, Response: {responseBody}");
        }

        var result = JsonSerializer.Deserialize<ContainerStatusResponse>(responseBody, JsonOptions);
        return result?.StatusCode ?? "UNKNOWN";
    }

    private async Task<bool> WaitForProcessingAsync(HttpClient httpClient, string containerId, string label, ILogger logger, int maxWaitSeconds = 900)
    {
        logger.LogInformation("Waiting for container {containerId} ({label}) to process...", containerId, label);

        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalSeconds < maxWaitSeconds)
        {
            var status = await GetContainerStatusAsync(httpClient, containerId);
            logger.LogInformation("Container {containerId} ({label}) status: {status}", containerId, label, status);

            if (status == "FINISHED")
            {
                return true;
            }

            if (status == "ERROR")
            {
                logger.LogError("Container {containerId} ({label}) processing failed with ERROR status", containerId, label);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        logger.LogError("Timeout waiting for container {containerId} ({label}) after {maxWait}s", containerId, label, maxWaitSeconds);
        return false;
    }

    private async Task<string> CreateCarouselContainerAsync(HttpClient httpClient, string accountId, List<string> childContainerIds, string caption, ILogger logger)
    {
        var encodedToken = Uri.EscapeDataString(_accessToken);
        var encodedCaption = Uri.EscapeDataString(caption);
        var childrenParam = string.Join(",", childContainerIds);

        var url = $"https://graph.facebook.com/{_apiVersion}/{accountId}/media" +
                  $"?media_type=CAROUSEL&children={childrenParam}&caption={encodedCaption}&access_token={encodedToken}";

        var response = await httpClient.PostAsync(url, null);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to create carousel container. Status: {response.StatusCode}, Response: {responseBody}");
        }

        var result = JsonSerializer.Deserialize<GraphApiIdResponse>(responseBody, JsonOptions);
        if (result == null || string.IsNullOrEmpty(result.Id))
        {
            throw new InvalidOperationException($"No carousel container ID returned. Response: {responseBody}");
        }

        return result.Id;
    }

    private async Task<string> PublishMediaAsync(HttpClient httpClient, string accountId, string carouselContainerId, ILogger logger)
    {
        var encodedToken = Uri.EscapeDataString(_accessToken);
        var url = $"https://graph.facebook.com/{_apiVersion}/{accountId}/media_publish" +
                  $"?creation_id={carouselContainerId}&access_token={encodedToken}";

        var response = await httpClient.PostAsync(url, null);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to publish carousel. Status: {response.StatusCode}, Response: {responseBody}");
        }

        var result = JsonSerializer.Deserialize<GraphApiIdResponse>(responseBody, JsonOptions);
        if (result == null || string.IsNullOrEmpty(result.Id))
        {
            throw new InvalidOperationException($"No media ID returned. Response: {responseBody}");
        }

        return result.Id;
    }

    private async Task<string?> GetPermalinkAsync(HttpClient httpClient, string mediaId, ILogger logger)
    {
        try
        {
            var encodedToken = Uri.EscapeDataString(_accessToken);
            var url = $"https://graph.facebook.com/{_apiVersion}/{mediaId}" +
                      $"?fields=permalink&access_token={encodedToken}";

            var response = await httpClient.GetAsync(url);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch permalink for media {mediaId}. Status: {status}", mediaId, response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<PermalinkResponse>(responseBody, JsonOptions);
            var permalink = result?.Permalink;
            logger.LogInformation("Carousel permalink: {permalink}", permalink);
            return permalink;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch permalink for media {mediaId} (non-critical)", mediaId);
            return null;
        }
    }

    private class PermalinkResponse
    {
        public string? Permalink { get; set; }
    }
}
