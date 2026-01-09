using System.Net;
using System.Text.Json;

namespace ArticleSimplifier;

public class ArticleSimplifierFunction
{
    private readonly ILogger _logger;
    private readonly ChatClient _chatClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ArticleSimplifierFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ArticleSimplifierFunction>();

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");

        _chatClient = new ChatClient(model: "gpt-4o", apiKey: apiKey);
    }

    [Function("ArticleSimplifier")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Processing article simplification request");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ArticleSimplifierRequest>(requestBody, JsonOptions);

            if (request == null || string.IsNullOrEmpty(request.ArticleUrl))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid request. Please provide 'articleUrl' and 'audienceAge'.");
                return badResponse;
            }

            _logger.LogInformation("Simplifying article: {ArticleUrl} for audience age: {AudienceAge}",
                request.ArticleUrl, request.AudienceAge);

            // Step 1: Fetch article content and title
            var (articleTitle, articleContent) = await FetchArticleContentAsync(request.ArticleUrl);
            _logger.LogInformation("Fetched article: '{Title}' ({Length} characters)", articleTitle, articleContent.Length);

            if (string.IsNullOrWhiteSpace(articleContent))
            {
                var emptyResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await emptyResponse.WriteStringAsync("Could not extract content from the provided URL.");
                return emptyResponse;
            }

            // Step 2: Use Azure OpenAI to simplify the article
            var simplifiedArticle = await SimplifyArticleWithOpenAIAsync(articleContent, request.AudienceAge);
            _logger.LogInformation("Article simplified successfully");

            // Step 3: Return the simplified article
            var response = req.CreateResponse(HttpStatusCode.OK);

            var result = new ArticleSimplifierResponse
            {
                OriginalUrl = request.ArticleUrl,
                AudienceAge = request.AudienceAge,
                Title = articleTitle,
                SimplifiedArticle = simplifiedArticle
            };

            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing article simplification");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<(string Title, string Content)> FetchArticleContentAsync(string articleUrl)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var html = await httpClient.GetStringAsync(articleUrl);

        // Parse HTML and extract main content
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        // Extract title (prioritize article-specific titles over page title)
        var title = string.Empty;
        var titleSelectors = new[]
        {
            "//*[@property='og:title']/@content",
            "//h1",
            "//*[@class='article-title']",
            "//*[@class='entry-title']",
            "//*[@class='post-title']",
            "//article//h1",
            "//title"
        };

        foreach (var selector in titleSelectors)
        {
            var titleNode = htmlDoc.DocumentNode.SelectSingleNode(selector);
            if (titleNode != null)
            {
                title = selector.EndsWith("/@content")
                    ? titleNode.GetAttributeValue("content", string.Empty)
                    : titleNode.InnerText.Trim();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    title = HtmlEntity.DeEntitize(title);
                    break;
                }
            }
        }

        // Try multiple common article content selectors
        var contentSelectors = new[]
        {
            "//article",
            "//*[@class='article-body']",
            "//*[@class='story-body']",
            "//*[@class='entry-content']",
            "//*[@class='post-content']",
            "//*[@id='article-body']",
            "//main",
            "//*[contains(@class, 'article')]//p",
            "//p"
        };

        string? content = null;
        foreach (var selector in contentSelectors)
        {
            var nodes = htmlDoc.DocumentNode.SelectNodes(selector);
            if (nodes != null && nodes.Any())
            {
                var paragraphs = nodes
                    .Where(n => n.Name == "p" || n.Descendants("p").Any())
                    .SelectMany(n => n.Name == "p" ? new[] { n } : n.Descendants("p"))
                    .Select(p => p.InnerText.Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text) && text.Length > 50)
                    .Take(20); // Take first 20 paragraphs to limit content size

                content = string.Join("\n\n", paragraphs);
                if (!string.IsNullOrWhiteSpace(content) && content.Length > 200)
                {
                    break;
                }
            }
        }

        // Decode HTML entities
        content = HtmlEntity.DeEntitize(content ?? string.Empty);

        return (title, content);
    }

    private async Task<string> SimplifyArticleWithOpenAIAsync(string articleContent, int audienceAge)
    {
        var ageDescription = audienceAge switch
        {
            < 8 => "young children (kindergarten level)",
            < 11 => "elementary school students",
            < 14 => "middle school students",
            < 18 => "high school students",
            _ => "adult readers"
        };

        var prompt = $@"You are an expert writer who specializes in making complex news articles accessible to different age groups.

Please rewrite the following news article for {ageDescription} (age {audienceAge}).

Requirements:
- Keep it to EXACTLY ONE PARAGRAPH only
- Keep the article in its ORIGINAL LANGUAGE - do NOT translate it
- Use age-appropriate vocabulary and sentence structure
- Maintain the key facts and main points
- Make it engaging and easy to understand
- Remove any inappropriate content for this age group

Original Article:
{articleContent}

Simplified Article:";

        try
        {
            var completion = await _chatClient.CompleteChatAsync(
                [new UserChatMessage(prompt)],
                new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 300,
                    Temperature = 0.7f
                });

            return completion.Value.Content[0].Text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI API error: {Message}", ex.Message);
            throw new HttpRequestException($"OpenAI API error: {ex.Message}", ex);
        }
    }
}

public class ArticleSimplifierRequest
{
    public string ArticleUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
}

public class ArticleSimplifierResponse
{
    public string OriginalUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
}
