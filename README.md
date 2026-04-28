# AI Newspaper

An AI-powered newspaper generation system that fetches news, rewrites it for specific age groups, generates illustrations, produces video narrations, and publishes to Instagram — all orchestrated by Azure Durable Functions.

## Project Structure

- `lambdas/NewspaperOrchestrator/` - Azure Durable Function that orchestrates the entire pipeline (all processing steps are built-in activities)
- `containers/VideoGenerator/` - Containerized video generation service (Azure Container App)
- `scripts/` - Azure setup and maintenance scripts
- `scripts-local/` - Local development/testing scripts
- `public-files/` - Static public assets
- `tests/` - Test data and local test harnesses

## Pipeline

The orchestrator runs the following steps as durable activities:

1. **Fetch Articles** — parses an RSS feed and selects the top 3 articles for the target age group (GPT-4o)
2. **Simplify Articles** — rewrites each article in age-appropriate language (parallel, Claude)
3. **Generate Images + Audio** — creates DALL-E 3 illustrations and text-to-speech audio for each article (parallel)
4. **Save Article JSONs** — persists each article's data to Azure Blob Storage
5. **Generate Videos** — sends articles to the VideoGenerator container app, polls for completion
6. **Publish to Instagram** — publishes the videos as an Instagram carousel
7. **Persist Batch Result** — saves the final batch result JSON to storage

On failure, the orchestrator persists the partial result and sends a notification email.

## Scheduling

Timer triggers run automatically. Times are Prague (CET, UTC+1) / UTC:

| Trigger | Age | Weekdays | Saturday | Sunday |
|---------|-----|----------|----------|--------|
| `DailyNewspaperScheduler_Age12_*` | 12 | 12:00 / 11:00 | 13:00 / 12:00 | 20:00 / 19:00 |
| `DailyNewspaperScheduler_Age16_*` | 16 | 20:00 / 19:00 | 21:00 / 20:00 | 21:00 / 20:00 |
| `DailyNewspaperScheduler_Age35_*` | 35 | 19:00 / 18:00 | 13:00 / 12:00 | 21:00 / 20:00 |
| `DailyNewspaperScheduler_Age65_*` | 65 | 09:00 / 08:00 | 13:00 / 12:00 | 16:00 / 15:00 |

Each run stores output in `age-{age}/{yyyy-MM-dd}/` in the `batch-runs` blob container.

## Local Development

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Azurite (local storage emulator)
- Docker (for VideoGenerator)

### Setup

1. Configure the orchestrator:
   ```bash
   cd lambdas/NewspaperOrchestrator
   cp local.settings.json.template local.settings.json
   # Edit local.settings.json with your API keys
   ```

2. Use the VS Code launch configurations:
   - **Orchestrator** — starts Azurite + NewspaperOrchestrator (port 7074)
   - **Orchestrator + VideoGenerator** — also builds and runs the VideoGenerator Docker container

### API Endpoints (Port 7074)

**Start batch processing:**
```bash
POST http://localhost:7074/api/StartNewspaperBatch
Content-Type: application/json

{
  "rssUrl": "https://www.ceskenoviny.cz/sluzby/rss/zpravy.php",
  "audienceAge": 12,
  "storageFolder": "age-12/2026-01-15"
}
```

**Check status:**
```bash
GET http://localhost:7074/api/status/{instanceId}
```

## Deployment

Three GitHub Actions workflows deploy on push to `master`:

| Workflow | Trigger Path | Target |
|----------|-------------|--------|
| `deploy-orchestrator.yml` | `lambdas/NewspaperOrchestrator/**` | Azure Function App |
| `deploy-video-generator-aca.yml` | `containers/VideoGenerator/**` | Azure Container App |
| `deploy-public-files.yml` | `public-files/**` | Azure Blob Storage |

### Required GitHub Secrets

- `AZURE_CREDENTIALS` — Service principal for Azure login
- `OPENAI_API_KEY` — OpenAI API key
- `DEFAULT_RSS_URL` — RSS feed URL
- `INSTAGRAM_ACCESS_TOKEN`, `INSTAGRAM_ACCOUNT_ID_12`, `INSTAGRAM_ACCOUNT_ID_16`, `INSTAGRAM_ACCOUNT_ID_35`, `INSTAGRAM_ACCOUNT_ID_65` — Instagram Graph API
- `EMAIL_CONNECTION_STRING`, `NOTIFICATION_EMAIL_TO`, `NOTIFICATION_EMAIL_FROM` — Azure Communication Services

## Security

Never commit `local.settings.json` files containing API keys — they are git-ignored.
