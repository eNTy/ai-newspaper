# AI Newspaper

An AI-powered newspaper generation system using Azure Functions and Claude AI to create age-appropriate news content.

## Project Structure

- `lambdas/` - Azure Functions for AI processing
  - `RssProcessor/` - Fetches RSS feeds and selects top 3 articles for target age
  - `ArticleSimplifier/` - Simplifies articles for specific age groups
  - `ImageGenerator/` - Generates illustrations for articles using Claude AI
- `infrastructure/` - VM and infrastructure configuration
- `scripts/` - Utility scripts

## Features

- **RSS Processing**: Fetches news from any RSS feed and uses Claude AI to select the most appropriate articles for a target age group
- **Article Simplification**: Rewrites articles in age-appropriate language (maintains original language, no translation)
- **Image Generation**: Creates illustrations based on article content (with Claude AI descriptions, ready for DALL-E integration)
- **Azure Storage**: Stores generated images in Azure Blob Storage

## Setup

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Claude API Key (get from https://console.anthropic.com/)
- Azure Storage Account (or Azurite for local development)

### Local Development Setup

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd ai-newspaper
   ```

2. Configure each Azure Function:
   ```bash
   # For each function (RssProcessor, ArticleSimplifier, ImageGenerator):
   cd lambdas/<FunctionName>

   # Copy the template and add your API key
   cp local.settings.json.template local.settings.json

   # Edit local.settings.json and replace "your-claude-api-key-here" with your actual key
   ```

3. Build and run:
   ```bash
   cd lambdas/<FunctionName>
   dotnet build
   cd bin/Debug/net8.0
   func start --no-build
   ```

## API Endpoints

### RssProcessor (Port 7071)
```bash
POST http://localhost:7071/api/RssProcessor
{
  "rssUrl": "https://example.com/rss",
  "audienceAge": 12
}
```

### ArticleSimplifier (Port 7072)
```bash
POST http://localhost:7072/api/ArticleSimplifier
{
  "articleUrl": "https://example.com/article",
  "audienceAge": 12
}
```

### ImageGenerator (Port 7073)
```bash
POST http://localhost:7073/api/ImageGenerator
{
  "articleTitle": "Article Title",
  "simplifiedArticle": "Article text...",
  "audienceAge": 12,
  "storageFolder": "images"
}
```

## Security

**Important**: Never commit `local.settings.json` files containing API keys. These files are ignored by git.

To configure for production:
```bash
az functionapp config appsettings set \
  --name <function-app-name> \
  --resource-group <resource-group> \
  --settings "CLAUDE_API_KEY=your-key"
```

## Deployment

TBD
