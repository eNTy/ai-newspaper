using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace NewspaperOrchestrator.Activities;

public class ArticleSimplifierActivity
{
    private readonly ChatClient _chatClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public ArticleSimplifierActivity(ChatClient chatClient, IHttpClientFactory httpClientFactory)
    {
        _chatClient = chatClient;
        _httpClientFactory = httpClientFactory;
    }

    [Function(nameof(SimplifyArticle))]
    public async Task<ProcessedArticle> SimplifyArticle(
        [ActivityTrigger] ArticleActivityInput input,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SimplifyArticle));
        var article = input.Article;

        logger.LogInformation("Simplifying article: {url} for age: {age}", article.Url, input.AudienceAge);

        // Step 1: Fetch article content and title
        var (articleTitle, articleContent) = await FetchArticleContentAsync(article.Url, logger);
        logger.LogInformation("Fetched article: '{Title}' ({Length} characters)", articleTitle, articleContent.Length);

        if (string.IsNullOrWhiteSpace(articleContent))
        {
            throw new InvalidOperationException($"Could not extract content from: {article.Url}");
        }

        // Step 2: Simplify with OpenAI
        var simplifiedArticle = await SimplifyArticleWithOpenAIAsync(articleContent, input.AudienceAge, logger);
        logger.LogInformation("Article simplified successfully");

        article.Title = articleTitle;
        article.SimplifiedArticle = simplifiedArticle;

        return article;
    }

    private async Task<(string Title, string Content)> FetchArticleContentAsync(string articleUrl, ILogger logger)
    {
        var httpClient = _httpClientFactory.CreateClient("ArticleFetcher");

        string html;
        try
        {
            var response = await httpClient.GetAsync(articleUrl);
            logger.LogInformation("HTTP Response Status: {status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Failed to fetch {url}. Status: {status}. Response: {response}",
                    articleUrl, response.StatusCode, errorContent[..Math.Min(500, errorContent.Length)]);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode} when fetching {articleUrl}");
            }

            html = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Successfully fetched {length} characters from {url}", html.Length, articleUrl);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("HTTP request failed for {url}: {message}", articleUrl, ex.Message);
            throw;
        }

        // Parse HTML and extract main content
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        // Extract title
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
                    .Take(20);

                content = string.Join("\n\n", paragraphs);
                if (!string.IsNullOrWhiteSpace(content) && content.Length > 200)
                {
                    break;
                }
            }
        }

        content = HtmlEntity.DeEntitize(content ?? string.Empty);

        return (title, content);
    }

    private async Task<string> SimplifyArticleWithOpenAIAsync(string articleContent, int audienceAge, ILogger logger)
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
- The output language MUST be always Czech!
- Keep it to EXACTLY ONE PARAGRAPH only
- This article will be READ ALOUD and must take NO MORE THAN 30 SECONDS to read
- Keep it SHORT and CONCISE (approximately 60-80 words maximum)
- Keep the article in its ORIGINAL LANGUAGE - do NOT translate it
- Use age-appropriate vocabulary and sentence structure
- Maintain only the most important key facts and main points
- Make it engaging and easy to understand
- Remove any inappropriate content for this age group

Original Article:
{articleContent}

Simplified Article:";

        var completion = await _chatClient.CompleteChatAsync(
            [new UserChatMessage(prompt)],
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = 300,
                Temperature = 0.7f
            });

        return completion.Value.Content[0].Text.Trim();
    }
}
