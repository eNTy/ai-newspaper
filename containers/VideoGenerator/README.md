# VideoGenerator Container App

ASP.NET Core Web API service for batch video generation using FFMPEG. Deployed as an Azure Container App with scale-to-zero capability.

## Features

- **Async Job Processing**: Submit a batch, get a job ID, poll for status
- **Batch Processing**: Generate multiple videos in a single job
- **FFMPEG Integration**: Pan/zoom effects, title overlay, matched to audio duration
- **C2PA Metadata**: Embeds content provenance metadata via c2patool
- **Scale-to-Zero**: Automatic scaling from 0–10 instances
- **Per-Folder Error Handling**: Individual folders can fail without aborting the batch

## API Endpoints

### Health Check
```http
GET /health
```
Returns `{ "status": "healthy", "timestamp": "..." }`

### Trigger Video Generation (async)
```http
POST /api/generate
Content-Type: application/json

{
  "storageFolders": [
    "age-12/2026-01-15/article-0",
    "age-12/2026-01-15/article-1",
    "age-12/2026-01-15/article-2"
  ]
}
```

Returns `202 Accepted` with a job ID:
```json
{
  "jobId": "abc123...",
  "status": "Queued",
  "message": "Job queued successfully. Use the jobId to check status."
}
```

### Check Job Status
```http
GET /api/generate/status/{jobId}
```

Returns progress and results:
```json
{
  "jobId": "abc123...",
  "status": "Completed",
  "createdAt": "2026-01-15T10:00:00Z",
  "completedAt": "2026-01-15T10:05:00Z",
  "totalFolders": 3,
  "processedFolders": 3,
  "results": [
    { "folder": "age-12/2026-01-15/article-0", "success": true, "videoUrl": "https://..." },
    { "folder": "age-12/2026-01-15/article-1", "success": true, "videoUrl": "https://..." },
    { "folder": "age-12/2026-01-15/article-2", "success": false, "error": "..." }
  ]
}
```

Job statuses: `Queued` → `Processing` → `Completed` | `Failed`

### Input Requirements

Each storage folder must contain these blobs in the `batch-runs` container:
- `image.png` — article illustration
- `speech.mp3` — audio narration
- `article.json` — article metadata (optional, used for title overlay)

## Video Specifications

- **Format**: MP4 (H.264 + AAC)
- **Resolution**: 1080×1350 (4:5 portrait, Instagram)
- **Frame Rate**: 25 FPS
- **Duration**: Matched to audio length
- **Effects**: Random per video — zoom in, zoom out, pan left, pan right
- **Title Overlay**: Centered white text on semi-transparent black box (if title available)
- **C2PA**: Content provenance metadata embedded via c2patool

## Local Development

### Prerequisites
- Docker Desktop
- PowerShell
- Azurite (for local storage emulation)

### Quick Start

1. **Build and run** (or use VS Code task `docker build and run: VideoGenerator`):
   ```powershell
   .\build-and-run.ps1 -Build -Run
   ```

2. **Test the API**:
   ```powershell
   Invoke-RestMethod -Uri "http://localhost:8080/health" -Method Get
   .\test-api.ps1 -StorageFolders "test-folder-1,test-folder-2"
   ```

3. **View logs**:
   ```powershell
   docker logs -f videogenerator-local
   ```

4. **Stop**:
   ```powershell
   .\build-and-run.ps1 -Stop
   ```

### VS Code Integration

Use the launch configurations from the root workspace:
- **Orchestrator + VideoGenerator** — starts Azurite, the orchestrator, and builds/runs the VideoGenerator container

## Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `AzureWebJobsStorage` | Azure Storage connection string | (required) |
| `BLOB_CONTAINER_NAME` | Blob container name | `batch-runs` |
| `ASPNETCORE_URLS` | Listening URL | `http://+:8080` |

For local development with Azurite, the connection string uses `host.docker.internal:10000` to reach the host.

## Architecture

### Request Flow

1. `POST /api/generate` — creates a job, returns job ID immediately (202)
2. Background task processes folders sequentially (global semaphore, one FFMPEG at a time)
3. For each folder: download blobs → ffprobe audio duration → FFMPEG video → c2patool C2PA → upload video
4. Orchestrator polls `GET /api/generate/status/{jobId}` until `Completed` or `Failed`

### Concurrency

- **Global semaphore** ensures only one FFMPEG process runs per instance (prevents OOM)
- **In-memory job store** tracks job state (ConcurrentDictionary)
- Multiple requests queue up; each waits for the semaphore
- Azure Container Apps scales horizontally (0–10 replicas), so up to 10 videos can process in parallel across instances

### Error Handling

- Errors are per-folder, not per-batch
- Failed folders get `success: false` with error message; successful ones get `success: true` with video URL
- Batch continues even if individual folders fail
- Job-level failures (e.g. missing storage connection) set the whole job to `Failed`

## Azure Deployment

Deployed via GitHub Actions (`deploy-video-generator-aca.yml`) on push to `master` when `containers/VideoGenerator/**` changes.

See [DEPLOYMENT.md](DEPLOYMENT.md) for initial setup details.

## Monitoring

Application Insights is enabled for structured logging. Query logs in Azure Portal:

```kusto
// All VideoGenerator traces
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| order by timestamp desc
| take 100

// Errors only
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| where severityLevel >= 3
| order by timestamp desc
```

Container logs via CLI:
```bash
az containerapp logs show \
  --name ai-newspaper-video-generator \
  --resource-group ai-newspaper-rg \
  --tail 100 --follow
```

## Troubleshooting

### 504 Gateway Timeout from Orchestrator
Azure Container Apps has a hard 240-second ingress timeout. The async job pattern avoids this — the orchestrator polls instead of waiting for a synchronous response. See [TIMEOUT-ISSUES.md](TIMEOUT-ISSUES.md) for details.

### Container won't start
- Check Docker is running: `docker ps`
- Check Azurite is accessible: `http://localhost:10000/devstoreaccount1`
- View container logs: `docker logs videogenerator-local`

### FFMPEG errors
- Image must be PNG format
- Audio must be MP3 format
- Audio duration must be > 0 seconds
- Verify blobs exist at expected paths in storage

### Cold start in Azure
First request after idle may take 10–30 seconds (scale-to-zero). The orchestrator warms up the container with a `/health` ping at the start of each orchestration.

## Project Structure

```
containers/VideoGenerator/
├── Program.cs              # API endpoints + video generation logic
├── VideoGenerator.csproj   # Project file
├── Dockerfile              # Multi-stage Docker build (includes FFMPEG + c2patool)
├── c2pa-manifest.json      # C2PA content provenance manifest
├── appsettings.json        # Configuration
├── build-and-run.ps1       # Local build/run/stop script
├── test-api.ps1            # API test script
├── DEPLOYMENT.md           # Azure deployment guide
├── CONCURRENCY.md          # Concurrency design notes
├── TIMEOUT-ISSUES.md       # Timeout troubleshooting
├── APPLICATION-INSIGHTS.md # Monitoring setup notes
└── README.md
```
