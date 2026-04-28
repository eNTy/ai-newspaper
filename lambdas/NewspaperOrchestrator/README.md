# Newspaper Orchestrator

Azure Durable Function that orchestrates the entire newspaper generation pipeline — from RSS fetching through to Instagram publishing.

## Architecture

All processing steps run as built-in durable activities within a single Azure Function App. The only external service call is to the VideoGenerator container app.

```
StartNewspaperBatch (HTTP Trigger) / DailyNewspaperScheduler (Timer Triggers)
        ↓
NewspaperBatchOrchestrator (Orchestrator)
        ↓
    ┌─────────┐
    │ Step 0  │ WarmupVideoGenerator (fire-and-forget, awaited before step 5)
    └─────────┘
    ┌─────────┐
    │ Step 1  │ FetchTopArticles — parse RSS, select top 3 via GPT-4o
    └────┬────┘
    ┌────┴────┐
    │ Step 2  │ SimplifyArticle ×3 (parallel) — rewrite for target age via Claude
    └────┬────┘
    ┌────┴────┐
    │ Step 3  │ GenerateImage ×3 + GenerateAudio ×3 (parallel) — DALL-E 3 + TTS
    └────┬────┘
    ┌────┴────┐
    │ Step 4  │ SaveArticleJson ×3 — persist article data to blob storage
    └────┬────┘
    ┌────┴────┐
    │ Step 5  │ GenerateVideos — async job to VideoGenerator container, poll for completion
    └────┬────┘
    ┌────┴────┐
    │ Step 6  │ PublishToInstagram — carousel of videos
    └────┬────┘
    ┌────┴────┐
    │ Step 7  │ SaveBatchResult — persist final result JSON
    └─────────┘
```

On failure at any step, the orchestrator saves the partial batch result and sends a notification email via Azure Communication Services.

## Activities

| Activity | Source | Description |
|----------|--------|-------------|
| `FetchTopArticles` | `RssProcessorActivity.cs` | Fetches RSS feed, uses GPT-4o to pick top 3 articles |
| `SimplifyArticle` | `ArticleSimplifierActivity.cs` | Rewrites article text for the target age group (Claude) |
| `GenerateImage` | `ImageGeneratorActivity.cs` | Generates a DALL-E 3 illustration, uploads to blob storage |
| `GenerateAudio` | `TextToSpeechActivity.cs` | Generates TTS audio, uploads to blob storage |
| `PublishToInstagram` | `InstagramPublisherActivity.cs` | Publishes video carousel via Instagram Graph API |
| `WarmupVideoGenerator` | `NewspaperOrchestratorFunction.cs` | Health-check ping to wake up the container app |
| `GenerateVideos` | `NewspaperOrchestratorFunction.cs` | Triggers async video generation job |
| `CheckVideoGenerationStatus` | `NewspaperOrchestratorFunction.cs` | Polls video generation job status |
| `SaveArticleJson` | `NewspaperOrchestratorFunction.cs` | Persists article JSON to blob storage |
| `SaveBatchResult` | `NewspaperOrchestratorFunction.cs` | Persists batch result JSON to blob storage |
| `SendFailureEmail` | `NewspaperOrchestratorFunction.cs` | Sends failure notification email |

## Configuration

Environment variables in `local.settings.json`:

| Variable | Description |
|----------|-------------|
| `AzureWebJobsStorage` | Storage connection for durable tasks (`UseDevelopmentStorage=true` locally) |
| `OPENAI_API_KEY` | OpenAI API key (GPT-4o + DALL-E 3) |
| `VIDEO_GENERATOR_URL` | VideoGenerator container app base URL |
| `BLOB_CONTAINER_NAME` | Blob container for output (`batch-runs`) |
| `DEFAULT_RSS_URL` | Default RSS feed for scheduled runs |
| `INSTAGRAM_ACCESS_TOKEN` | Instagram Graph API token |
| `INSTAGRAM_ACCOUNT_ID_12` | Instagram account for age 12 content |
| `INSTAGRAM_ACCOUNT_ID_16` | Instagram account for age 16 content |
| `INSTAGRAM_ACCOUNT_ID_35` | Instagram account for age 35 content |
| `INSTAGRAM_ACCOUNT_ID_65` | Instagram account for age 65 content |
| `EMAIL_CONNECTION_STRING` | Azure Communication Services connection string |
| `NOTIFICATION_EMAIL_TO` | Failure notification recipient |
| `NOTIFICATION_EMAIL_FROM` | Failure notification sender |

## Scheduling

All times are Prague (CET, UTC+1). During CEST (summer), triggers fire 1 hour earlier in local time.

| Function | CRON | Description |
|----------|------|-------------|
| `DailyNewspaperScheduler_Age12_Weekdays` | `0 0 11 * * 1-5` | Age 12, weekdays at 12:00 Prague |
| `DailyNewspaperScheduler_Age12_Saturday` | `0 0 12 * * 6` | Age 12, Saturday at 13:00 Prague |
| `DailyNewspaperScheduler_Age12_Sunday` | `0 0 19 * * 0` | Age 12, Sunday at 20:00 Prague |
| `DailyNewspaperScheduler_Age16_Weekdays` | `0 0 19 * * 1-5` | Age 16, weekdays at 20:00 Prague |
| `DailyNewspaperScheduler_Age16_Weekends` | `0 0 20 * * 0,6` | Age 16, weekends at 21:00 Prague |
| `DailyNewspaperScheduler_Age35_Weekdays` | `0 0 18 * * 1-5` | Age 35, weekdays at 19:00 Prague |
| `DailyNewspaperScheduler_Age35_Saturday` | `0 0 12 * * 6` | Age 35, Saturday at 13:00 Prague |
| `DailyNewspaperScheduler_Age35_Sunday` | `0 0 20 * * 0` | Age 35, Sunday at 21:00 Prague |
| `DailyNewspaperScheduler_Age65_Weekdays` | `0 0 8 * * 1-5` | Age 65, weekdays at 09:00 Prague |
| `DailyNewspaperScheduler_Age65_Saturday` | `0 0 12 * * 6` | Age 65, Saturday at 13:00 Prague |
| `DailyNewspaperScheduler_Age65_Sunday` | `0 0 15 * * 0` | Age 65, Sunday at 16:00 Prague |

## HTTP Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/StartNewspaperBatch` | POST | Start a manual batch orchestration |
| `/api/status/{instanceId}` | GET | Query orchestration status |
| `/api/TestEmail` | POST | Send a test notification email |
