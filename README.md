# AI Newspaper

An AI-powered newspaper generation system using Azure Functions and OpenAI to create age-appropriate news content.

## Project Structure

- `lambdas/` - Azure Functions for AI processing
  - `RssProcessor/` - Fetches RSS feeds and selects top 3 articles for target age
  - `ArticleSimplifier/` - Simplifies articles for specific age groups
  - `ImageGenerator/` - Generates illustrations for articles using DALL-E 3
  - `NewspaperOrchestrator/` - Orchestrates batch processing of all three functions
- `infrastructure/` - VM and infrastructure configuration
- `scripts/` - Utility scripts

## Features

- **RSS Processing**: Fetches news from any RSS feed and uses OpenAI (GPT-4o) to select the most appropriate articles for a target age group
- **Article Simplification**: Rewrites articles in age-appropriate language (maintains original language, no translation)
- **Image Generation**: Creates actual PNG illustrations using DALL-E 3
- **Batch Orchestration**: Durable function that processes all 3 articles in parallel for optimal performance
- **Azure Storage**: Stores generated images in Azure Blob Storage

## Setup

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- OpenAI API Key (get from https://platform.openai.com/)
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

   # Copy the template and add your OpenAI API key
   cp local.settings.json.template local.settings.json

   # Edit local.settings.json and add:
   # - OPENAI_API_KEY: Your OpenAI API key (starts with sk-)
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

### NewspaperOrchestrator (Port 7074) - Batch Processing
```bash
# Start batch processing
POST http://localhost:7074/api/StartNewspaperBatch
{
  "rssUrl": "https://example.com/rss",
  "audienceAge": 12,
  "storageFolder": "images"
}

# Check status
GET http://localhost:7074/api/status/{instanceId}
```

This orchestrator automatically:
1. Fetches top 3 articles from RSS
2. Simplifies all 3 articles in parallel
3. Generates images for all 3 in parallel
4. Returns complete batch result

See [lambdas/NewspaperOrchestrator/README.md](lambdas/NewspaperOrchestrator/README.md) for details.

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

The project uses GitHub Actions for continuous deployment to Azure Functions.

### Quick Start

1. Run the setup script:
   ```bash
   cd scripts
   chmod +x setup-azure-resources.sh
   ./setup-azure-resources.sh
   ```

2. Add the GitHub secrets shown in the script output to your repository

3. Push to master branch to trigger deployment

For detailed instructions, see [DEPLOYMENT.md](DEPLOYMENT.md)
