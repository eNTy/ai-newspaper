using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace InstagramPublisher;

public class InstagramPublisherFunction
{
    private readonly ILogger _logger;
    private readonly string _accessToken;
    private readonly string _apiVersion;
    private readonly string _storageConnectionString;
    private readonly string _blobContainerName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InstagramPublisherFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<InstagramPublisherFunction>();

        _accessToken = Environment.GetEnvironmentVariable("INSTAGRAM_ACCESS_TOKEN")
            ?? throw new InvalidOperationException("INSTAGRAM_ACCESS_TOKEN environment variable is not set");

        _apiVersion = Environment.GetEnvironmentVariable("INSTAGRAM_API_VERSION") ?? "v24.0";

        _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set");

        _blobContainerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable is not set");
    }

    private string GetAccountIdForAge(int audienceAge)
    {
        var envVar = $"INSTAGRAM_ACCOUNT_ID_{audienceAge}";
        return Environment.GetEnvironmentVariable(envVar)
            ?? throw new InvalidOperationException(
                $"Environment variable {envVar} is not set. Each age group requires its own Instagram account ID.");
    }

    [Function("InstagramPublisher")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Instagram carousel publisher triggered");

        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<InstagramPublishRequest>(requestBody, JsonOptions);

            if (request == null || string.IsNullOrEmpty(request.BatchResultPath))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid request. BatchResultPath is required.");
                return badResponse;
            }

            // Step 1: Download and parse the batch result JSON from blob storage
            _logger.LogInformation("Downloading batch result from: {path}", request.BatchResultPath);
            var batchResult = await DownloadBatchResultAsync(request.BatchResultPath);

            if (batchResult == null || batchResult.Articles == null || batchResult.Articles.Count == 0)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Batch result contains no articles.");
                return badResponse;
            }

            // Step 2: Extract video URLs from articles
            var videoUrls = batchResult.Articles
                .Where(a => !string.IsNullOrEmpty(a.VideoUrl))
                .Select(a => a.VideoUrl)
                .ToList();

            if (videoUrls.Count == 0)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("No video URLs found in batch result articles.");
                return badResponse;
            }

            _logger.LogInformation("Found {count} videos to publish", videoUrls.Count);

            // Step 3: Resolve Instagram account for this age group
            var accountId = GetAccountIdForAge(batchResult.AudienceAge);
            _logger.LogInformation("Using Instagram account {accountId} for age group {age}", accountId, batchResult.AudienceAge);

            // Step 4: Generate caption from article titles
            var caption = GenerateCaption(batchResult.Articles);
            _logger.LogInformation("Generated caption: {caption}", caption);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(20);

            // Step 5: Create child video containers
            _logger.LogInformation("Creating child video containers...");
            var childContainerIds = new List<string>();
            for (int i = 0; i < videoUrls.Count; i++)
            {
                var containerId = await CreateChildVideoContainerAsync(httpClient, accountId, videoUrls[i]);
                childContainerIds.Add(containerId);
                _logger.LogInformation("Created child container {id} for video {index}", containerId, i + 1);
            }

            // Step 6: Wait for all child containers to finish processing
            _logger.LogInformation("Waiting for all child video containers to process...");
            for (int i = 0; i < childContainerIds.Count; i++)
            {
                var success = await WaitForProcessingAsync(httpClient, childContainerIds[i], $"video {i + 1}");
                if (!success)
                {
                    throw new InvalidOperationException($"Video {i + 1} container processing failed (container: {childContainerIds[i]})");
                }
            }

            // Step 7: Create carousel container
            _logger.LogInformation("Creating carousel container...");
            var carouselContainerId = await CreateCarouselContainerAsync(httpClient, accountId, childContainerIds, caption);
            _logger.LogInformation("Created carousel container: {id}", carouselContainerId);

            // Step 8: Wait for carousel to be ready
            _logger.LogInformation("Waiting for carousel to be ready...");
            var carouselReady = await WaitForProcessingAsync(httpClient, carouselContainerId, "carousel");
            if (!carouselReady)
            {
                throw new InvalidOperationException($"Carousel container processing failed (container: {carouselContainerId})");
            }

            // Step 9: Publish the carousel
            _logger.LogInformation("Publishing carousel...");
            var mediaId = await PublishMediaAsync(httpClient, accountId, carouselContainerId);
            _logger.LogInformation("Successfully published carousel! Media ID: {mediaId}", mediaId);

            var result = new InstagramPublishResponse
            {
                Success = true,
                MediaId = mediaId
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish Instagram carousel: {message}", ex.Message);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new InstagramPublishResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            });
            return errorResponse;
        }
    }

    private async Task<BatchResult?> DownloadBatchResultAsync(string blobPath)
    {
        var blobServiceClient = new BlobServiceClient(_storageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_blobContainerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        var downloadResult = await blobClient.DownloadContentAsync();
        var json = downloadResult.Value.Content.ToString();

        return JsonSerializer.Deserialize<BatchResult>(json, JsonOptions);
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

    private async Task<string> CreateChildVideoContainerAsync(HttpClient httpClient, string accountId, string videoUrl)
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

    private async Task<bool> WaitForProcessingAsync(HttpClient httpClient, string containerId, string label, int maxWaitSeconds = 900)
    {
        _logger.LogInformation("Waiting for container {containerId} ({label}) to process...", containerId, label);

        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalSeconds < maxWaitSeconds)
        {
            var status = await GetContainerStatusAsync(httpClient, containerId);
            _logger.LogInformation("Container {containerId} ({label}) status: {status}", containerId, label, status);

            if (status == "FINISHED")
            {
                return true;
            }

            if (status == "ERROR")
            {
                _logger.LogError("Container {containerId} ({label}) processing failed with ERROR status", containerId, label);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        _logger.LogError("Timeout waiting for container {containerId} ({label}) after {maxWait}s", containerId, label, maxWaitSeconds);
        return false;
    }

    private async Task<string> CreateCarouselContainerAsync(HttpClient httpClient, string accountId, List<string> childContainerIds, string caption)
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

    private async Task<string> PublishMediaAsync(HttpClient httpClient, string accountId, string carouselContainerId)
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
}

// Request/Response models for this function
public class InstagramPublishRequest
{
    public string BatchResultPath { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string StorageFolder { get; set; } = string.Empty;
}

public class InstagramPublishResponse
{
    public bool Success { get; set; }
    public string? MediaId { get; set; }
    public string? ErrorMessage { get; set; }
}

// Subset of the orchestrator's BatchResult for deserialization
public class BatchResult
{
    public bool Success { get; set; }
    public int AudienceAge { get; set; }
    public List<ProcessedArticle> Articles { get; set; } = new();
}

public class ProcessedArticle
{
    public string Title { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
}

// Instagram Graph API response models
public class GraphApiIdResponse
{
    public string Id { get; set; } = string.Empty;
}

public class ContainerStatusResponse
{
    [JsonPropertyName("status_code")]
    public string StatusCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
