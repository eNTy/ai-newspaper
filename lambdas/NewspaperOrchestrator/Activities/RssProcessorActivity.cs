using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace NewspaperOrchestrator.Activities;

public class RssProcessorActivity
{
    private readonly ChatClient _chatClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public RssProcessorActivity(ChatClient chatClient, IHttpClientFactory httpClientFactory)
    {
        _chatClient = chatClient;
        _httpClientFactory = httpClientFactory;
    }

    [Function(nameof(FetchTopArticles))]
    public async Task<List<ProcessedArticle>> FetchTopArticles(
        [ActivityTrigger] BatchResult batchResult,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(FetchTopArticles));
        logger.LogInformation("Fetching top articles from: {url} for age: {age}", batchResult.RssUrl, batchResult.AudienceAge);

        // Step 1: Fetch RSS feed items
        var feedItems = await FetchRssFeedAsync(batchResult.RssUrl);
        logger.LogInformation("Fetched {count} items from RSS feed", feedItems.Count);

        if (feedItems.Count == 0)
        {
            return new List<ProcessedArticle>();
        }

        // Step 2: Ask OpenAI to select top 3 articles
        var topArticles = await GetTopArticlesFromOpenAIAsync(feedItems, batchResult.AudienceAge, logger);
        logger.LogInformation("OpenAI selected {count} top articles", topArticles.Count);

        // Step 3: Map to ProcessedArticle with StorageFolder set
        var result = new List<ProcessedArticle>();
        for (int i = 0; i < topArticles.Count; i++)
        {
            result.Add(new ProcessedArticle
            {
                Url = topArticles[i].Link,
                StorageFolder = $"{batchResult.StorageFolder}/article-{i}"
            });
        }

        return result;
    }

    private async Task<List<RssFeedItem>> FetchRssFeedAsync(string rssUrl)
    {
        var httpClient = _httpClientFactory.CreateClient();
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

    private async Task<List<RssFeedItem>> GetTopArticlesFromOpenAIAsync(
        List<RssFeedItem> items, int audienceAge, ILogger logger)
    {
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

        var completion = await _chatClient.CompleteChatAsync(
            [new UserChatMessage(prompt)],
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = 100,
                Temperature = 0.7f
            });

        var selectedIndicesText = completion.Value.Content[0].Text.Trim();
        logger.LogInformation("OpenAI response: {Response}", selectedIndicesText);

        // Extract JSON array from response
        var startIdx = selectedIndicesText.IndexOf('[');
        var endIdx = selectedIndicesText.LastIndexOf(']');
        if (startIdx >= 0 && endIdx > startIdx)
        {
            selectedIndicesText = selectedIndicesText.Substring(startIdx, endIdx - startIdx + 1);
        }

        var selectedIndices = JsonSerializer.Deserialize<List<int>>(selectedIndicesText) ?? new List<int>();

        return selectedIndices
            .Where(idx => idx > 0 && idx <= items.Count)
            .Select(idx => items[idx - 1])
            .Take(3)
            .ToList();
    }
}
