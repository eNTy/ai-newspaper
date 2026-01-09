# Article Simplifier Azure Function

An Azure Function that fetches article content from a URL and uses Claude AI to simplify it for a specific audience age.

## Inputs

The function accepts a POST request with the following JSON body:

```json
{
  "articleUrl": "https://example.com/article",
  "audienceAge": 12
}
```

- `articleUrl` (string, required): The URL of the article to simplify
- `audienceAge` (int, required): The expected age of the target audience

## Output

Returns a JSON response with the simplified article:

```json
{
  "originalUrl": "https://example.com/article",
  "audienceAge": 12,
  "simplifiedArticle": "The simplified article content (1-2 paragraphs)..."
}
```

## Features

- Fetches full article content from any URL
- Extracts main article text from HTML
- Integrates with Claude AI API (Claude Sonnet 4.5) for intelligent simplification
- Adapts vocabulary and complexity based on audience age
- Returns simplified 1-2 paragraph version

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
cd lambdas/ArticleSimplifier
dotnet restore
dotnet build
cd bin/Debug/net8.0
func start --no-build
```

### Testing

```bash
curl -X POST http://localhost:7072/api/ArticleSimplifier \
  -H "Content-Type: application/json" \
  -d '{
    "articleUrl": "https://www.nytimes.com/2026/01/08/world/canada/gander-canada-airport-stranded-travelers.html",
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

1. **Fetch Article**: The function retrieves the HTML content from the provided URL
2. **Extract Content**: Uses HtmlAgilityPack to parse and extract the main article text
3. **Simplify with Claude**: Sends the article to Claude AI with age-specific instructions
4. **Return Result**: Returns a 1-2 paragraph simplified version appropriate for the target age

## Age-Appropriate Simplification

The function adapts the simplification based on age:
- **Under 8**: Kindergarten level language
- **8-10**: Elementary school level
- **11-13**: Middle school level
- **14-17**: High school level
- **18+**: Adult level with clarity focus
