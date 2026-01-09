# RSS Processor Azure Function

An Azure Function that processes RSS feeds and uses Claude AI to select the top 3 most interesting articles for a specific audience age.

## Inputs

The function accepts a POST request with the following JSON body:

```json
{
  "rssUrl": "https://example.com/rss",
  "audienceAge": 12
}
```

- `rssUrl` (string, required): The URL of the RSS feed to process
- `audienceAge` (int, required): The expected age of the target audience

## Output

Returns a JSON response with the top 3 articles selected by Claude AI:

```json
{
  "sourceUrl": "https://example.com/rss",
  "audienceAge": 12,
  "topArticles": [
    {
      "title": "Article Title 1",
      "url": "https://example.com/article1"
    },
    {
      "title": "Article Title 2",
      "url": "https://example.com/article2"
    },
    {
      "title": "Article Title 3",
      "url": "https://example.com/article3"
    }
  ]
}
```

## Features

- Fetches and parses RSS feeds from any valid RSS source
- Integrates with Claude AI API (Claude Sonnet 4.5) to analyze articles
- Selects the top 3 most interesting and age-appropriate articles
- Returns article titles and URLs for easy access

## Local Development

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Claude API key (get one from https://console.anthropic.com/)

### Configuration

1. Update `local.settings.json` with your Claude API key:
   ```json
   {
     "Values": {
       "CLAUDE_API_KEY": "your-actual-api-key-here"
     }
   }
   ```

### Running Locally

```bash
cd lambdas/RssProcessor
dotnet restore
func start
```

### Testing

```bash
curl -X POST http://localhost:7071/api/RssProcessor \
  -H "Content-Type: application/json" \
  -d '{
    "rssUrl": "https://rss.nytimes.com/services/xml/rss/nyt/World.xml",
    "audienceAge": 12
  }'
```

## Deployment

1. Deploy to Azure using Azure Functions Core Tools:
   ```bash
   func azure functionapp publish <YOUR_FUNCTION_APP_NAME>
   ```

2. Set the `CLAUDE_API_KEY` environment variable in Azure:
   ```bash
   az functionapp config appsettings set \
     --name <YOUR_FUNCTION_APP_NAME> \
     --resource-group <YOUR_RESOURCE_GROUP> \
     --settings "CLAUDE_API_KEY=your-actual-api-key-here"
   ```

## How It Works

1. **Fetch RSS Feed**: The function retrieves all articles from the specified RSS feed URL
2. **Submit to Claude AI**: Article titles and descriptions are sent to Claude AI with the target audience age
3. **AI Selection**: Claude analyzes the content considering:
   - Age-appropriateness
   - Relevance to the audience's interests
   - Educational value
   - Engagement potential
4. **Return Top 3**: The function returns the URLs of the 3 most suitable articles

## Future Enhancements

- Support for multiple RSS feeds in a single request
- Caching mechanism for frequently accessed feeds
- Article summarization for selected items
- Support for different Claude models based on complexity needs
