# Newspaper Orchestrator

Azure Durable Function that orchestrates the batch processing of news articles for age-appropriate consumption.

## Overview

This orchestrator coordinates the entire newspaper generation pipeline:

1. **Fetch Articles**: Calls RssProcessor to get top 3 articles for the target age
2. **Simplify Articles**: Processes all 3 articles in parallel through ArticleSimplifier
3. **Generate Images**: Creates illustrations for all 3 articles in parallel using ImageGenerator
4. **Combine Results**: Returns a complete batch result with all processed articles

## Architecture

```
StartNewspaperBatch (HTTP Trigger)
        ↓
NewspaperBatchOrchestrator (Orchestrator)
        ↓
    ┌───┴────┐
    │ Step 1 │ FetchTopArticles (Activity)
    └───┬────┘        → Calls RssProcessor
        ↓
    ┌───┴────┐
    │ Step 2 │ SimplifyArticle (Activity x3, parallel)
    └───┬────┘        → Calls ArticleSimplifier
        ↓
    ┌───┴────┐
    │ Step 3 │ GenerateImage (Activity x3, parallel)
    └───┬────┘        → Calls ImageGenerator
        ↓
    BatchResult
```

## Local Development

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Azurite (for local storage emulation)
- All three base functions running (RssProcessor, ArticleSimplifier, ImageGenerator)

### Setup

1. Start Azurite for durable task storage:
   ```bash
   azurite --silent --location c:\azurite --debug c:\azurite\debug.log
   ```

2. Configure the function:
   ```bash
   cd lambdas/NewspaperOrchestrator
   cp local.settings.json.template local.settings.json
   # Edit local.settings.json if needed
   ```

3. Build and run:
   ```bash
   dotnet build
   cd bin/Debug/net8.0
   func start --no-build --port 7074
   ```

### Running the Full Pipeline Locally

You need all four functions running simultaneously:

**Terminal 1 - RssProcessor (port 7071)**:
```bash
cd lambdas/RssProcessor/bin/Debug/net8.0
func start --no-build
```

**Terminal 2 - ArticleSimplifier (port 7072)**:
```bash
cd lambdas/ArticleSimplifier/bin/Debug/net8.0
func start --no-build
```

**Terminal 3 - ImageGenerator (port 7073)**:
```bash
cd lambdas/ImageGenerator/bin/Debug/net8.0
func start --no-build
```

**Terminal 4 - NewspaperOrchestrator (port 7074)**:
```bash
cd lambdas/NewspaperOrchestrator/bin/Debug/net8.0
func start --no-build --port 7074
```

**Terminal 5 - Azurite**:
```bash
azurite
```

## API Usage

### Start Batch Processing

```bash
POST http://localhost:7074/api/StartNewspaperBatch
Content-Type: application/json

{
  "rssUrl": "https://www.ceskenoviny.cz/sluzby/rss/zpravy.php",
  "audienceAge": 12,
  "storageFolder": "images"
}
```

**Response (202 Accepted)**:
```json
{
  "instanceId": "abc123...",
  "statusQueryUrl": "http://localhost:7074/runtime/webhooks/durabletask/instances/abc123..."
}
```

### Check Status

```bash
GET http://localhost:7074/api/status/{instanceId}
```

**Response**:
```json
{
  "instanceId": "abc123...",
  "runtimeStatus": "Completed",
  "createdAt": "2026-01-09T10:00:00Z",
  "lastUpdatedAt": "2026-01-09T10:05:00Z",
  "output": {
    "articles": [
      {
        "url": "https://example.com/article1",
        "title": "Article Title 1",
        "simplifiedArticle": "Simplified text...",
        "imageUrl": "https://storage.blob.core.windows.net/images/...",
        "imageDescription": "Image description..."
      },
      {
        "url": "https://example.com/article2",
        "title": "Article Title 2",
        "simplifiedArticle": "Simplified text...",
        "imageUrl": "https://storage.blob.core.windows.net/images/...",
        "imageDescription": "Image description..."
      },
      {
        "url": "https://example.com/article3",
        "title": "Article Title 3",
        "simplifiedArticle": "Simplified text...",
        "imageUrl": "https://storage.blob.core.windows.net/images/...",
        "imageDescription": "Image description..."
      }
    ],
    "rssUrl": "https://www.ceskenoviny.cz/sluzby/rss/zpravy.php",
    "audienceAge": 12,
    "processedAt": "2026-01-09T10:05:00Z"
  }
}
```

### Runtime Status Values

- `Running`: Orchestration in progress
- `Completed`: Successfully finished
- `Failed`: An error occurred
- `Terminated`: Manually terminated
- `Pending`: Waiting to start

## Configuration

Environment variables in `local.settings.json`:

| Variable | Description | Default |
|----------|-------------|---------|
| `AzureWebJobsStorage` | Storage connection for durable tasks | `UseDevelopmentStorage=true` |
| `FUNCTIONS_WORKER_RUNTIME` | Runtime type | `dotnet-isolated` |
| `RSS_PROCESSOR_URL` | URL of RssProcessor function | `http://localhost:7071/api/RssProcessor` |
| `ARTICLE_SIMPLIFIER_URL` | URL of ArticleSimplifier function | `http://localhost:7072/api/ArticleSimplifier` |
| `IMAGE_GENERATOR_URL` | URL of ImageGenerator function | `http://localhost:7073/api/ImageGenerator` |

## Production Deployment

For production, configure the URLs to point to deployed Azure Functions:

```bash
az functionapp config appsettings set \
  --name ai-newspaper-orchestrator \
  --resource-group ai-newspaper-rg \
  --settings \
    "RSS_PROCESSOR_URL=https://ai-newspaper-rss-processor.azurewebsites.net/api/RssProcessor" \
    "ARTICLE_SIMPLIFIER_URL=https://ai-newspaper-article-simplifier.azurewebsites.net/api/ArticleSimplifier" \
    "IMAGE_GENERATOR_URL=https://ai-newspaper-image-generator.azurewebsites.net/api/ImageGenerator"
```

## Features

- **Parallel Processing**: Articles are simplified and illustrated in parallel for performance
- **Durable Execution**: State is persisted, surviving function restarts
- **Status Tracking**: Query orchestration status at any time
- **Automatic Retries**: Built-in retry policies for transient failures
- **Replay Safety**: Orchestrator code is deterministic and replay-safe

## Error Handling

If any activity fails:
- The orchestration will fail with detailed error information
- Check logs for specific activity failures
- Status endpoint shows error details

## Performance

Processing time depends on:
- RSS feed response time
- Article content length
- Claude AI API response times
- Number of articles (currently fixed at 3)

Typical processing time: 30-60 seconds for 3 articles

## Troubleshooting

### Orchestration stuck in "Pending"
- Ensure Azurite is running
- Check `AzureWebJobsStorage` connection string
- Verify storage account exists in production

### Activity functions fail
- Ensure all base functions are running
- Check URLs in configuration
- Verify network connectivity between functions

### "Replay mismatch" errors
- Don't use non-deterministic operations in orchestrator
- Use `context.CurrentUtcDateTime` instead of `DateTime.UtcNow`
- Use activity functions for external calls
