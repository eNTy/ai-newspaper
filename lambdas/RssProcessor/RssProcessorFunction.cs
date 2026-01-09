using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace RssProcessor;

public class RssProcessorFunction
{
    private readonly ILogger _logger;
    private readonly string _claudeApiKey;
    private const string CLAUDE_API_URL = "https://api.anthropic.com/v1/messages";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RssProcessorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RssProcessorFunction>();
        _claudeApiKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY")
            ?? throw new InvalidOperationException("CLAUDE_API_KEY environment variable is not set");
    }

    [Function("RssProcessor")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Processing RSS feed request");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<RssProcessorRequest>(requestBody, JsonOptions);

            if (request == null || string.IsNullOrEmpty(request.RssUrl))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid request. Please provide 'rssUrl' and 'audienceAge'.");
                return badResponse;
            }

            _logger.LogInformation($"Processing RSS feed: {request.RssUrl} for audience age: {request.AudienceAge}");

            // Step 1: Fetch RSS feed items
            var feedItems = await FetchRssFeedAsync(request.RssUrl);
            _logger.LogInformation($"Fetched {feedItems.Count} items from RSS feed");

            if (feedItems.Count == 0)
            {
                var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
                await emptyResponse.WriteAsJsonAsync(new RssProcessorResponse
                {
                    SourceUrl = request.RssUrl,
                    AudienceAge = request.AudienceAge,
                    TopArticles = new List<ArticleResult>()
                });
                return emptyResponse;
            }

            // Step 2: Ask Claude AI to select top 3 articles
            var topArticles = await GetTopArticlesFromClaudeAsync(feedItems, request.AudienceAge);
            _logger.LogInformation($"Claude AI selected {topArticles.Count} top articles");

            // Step 3: Return the top 3 URLs
            var response = req.CreateResponse(HttpStatusCode.OK);

            var result = new RssProcessorResponse
            {
                SourceUrl = request.RssUrl,
                AudienceAge = request.AudienceAge,
                TopArticles = topArticles
            };

            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RSS feed");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<List<RssFeedItem>> FetchRssFeedAsync(string rssUrl)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "AI-Newspaper-RSS-Processor/1.0");

        var feedContent = await httpClient.GetStringAsync(rssUrl);

        using var stringReader = new StringReader(feedContent);
        using var xmlReader = XmlReader.Create(stringReader);

        var feed = SyndicationFeed.Load(xmlReader);

        return feed.Items.Select(item => new RssFeedItem
        {
            Title = item.Title?.Text ?? "No Title",
            Description = item.Summary?.Text ?? string.Empty,
            Link = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
            PublishDate = item.PublishDate.DateTime,
            Categories = item.Categories.Select(c => c.Name).ToList()
        }).ToList();
    }

    private async Task<List<ArticleResult>> GetTopArticlesFromClaudeAsync(List<RssFeedItem> items, int audienceAge)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", _claudeApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        // Prepare the article list for Claude
        var articlesList = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            articlesList.AppendLine($"{i + 1}. Title: {items[i].Title}");
            if (!string.IsNullOrEmpty(items[i].Description))
            {
                articlesList.AppendLine($"   Description: {items[i].Description}");
            }
            articlesList.AppendLine();
        }

        var prompt = $@"You are analyzing news articles for a {audienceAge}-year-old audience.

Here are the available articles:

{articlesList}

Please select the TOP 3 articles that would be most interesting and appropriate for a {audienceAge}-year-old reader. Consider:
- Age-appropriateness of content
- Relevance to their interests and understanding level
- Educational value
- Engagement potential

Respond with ONLY a JSON array containing exactly 3 numbers (the article numbers from the list above), like this: [1, 5, 8]
Do not include any other text or explanation.";

        var claudeRequest = new
        {
            model = "claude-sonnet-4-5-20250929",
            max_tokens = 100,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var jsonContent = JsonSerializer.Serialize(claudeRequest);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(CLAUDE_API_URL, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude API error: {StatusCode} - {ResponseBody}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Claude API returned {response.StatusCode}: {responseBody}");
        }

        var claudeResponse = JsonSerializer.Deserialize<ClaudeApiResponse>(responseBody);

        if (claudeResponse?.Content == null || claudeResponse.Content.Length == 0)
        {
            throw new InvalidOperationException("Invalid response from Claude API");
        }

        var selectedIndicesText = claudeResponse.Content[0].Text?.Trim() ?? "[]";

        // Extract JSON array from response (handles cases where Claude might add extra text)
        var startIdx = selectedIndicesText.IndexOf('[');
        var endIdx = selectedIndicesText.LastIndexOf(']');
        if (startIdx >= 0 && endIdx > startIdx)
        {
            selectedIndicesText = selectedIndicesText.Substring(startIdx, endIdx - startIdx + 1);
        }

        var selectedIndices = JsonSerializer.Deserialize<List<int>>(selectedIndicesText) ?? new List<int>();

        // Convert 1-based indices to 0-based and get the articles
        var topArticles = selectedIndices
            .Where(idx => idx > 0 && idx <= items.Count)
            .Select(idx => items[idx - 1])
            .Take(3)
            .Select(item => new ArticleResult
            {
                Title = item.Title,
                Url = item.Link
            })
            .ToList();

        return topArticles;
    }
}

public class RssProcessorRequest
{
    public string RssUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
}

public class RssProcessorResponse
{
    public string SourceUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public List<ArticleResult> TopArticles { get; set; } = new();
}

public class ArticleResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class RssFeedItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTime PublishDate { get; set; }
    public List<string> Categories { get; set; } = new();
}

public class ClaudeApiResponse
{
    [JsonPropertyName("content")]
    public ClaudeContent[]? Content { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("usage")]
    public ClaudeUsage? Usage { get; set; }
}

public class ClaudeContent
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class ClaudeUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}
