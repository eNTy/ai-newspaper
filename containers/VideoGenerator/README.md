# VideoGenerator Container App

ASP.NET Core Web API service for batch video generation using FFMPEG. Deployed as an Azure Container App with scale-to-zero capability.

## Features

- **Batch Processing**: Generate multiple videos in a single API call
- **FFMPEG Integration**: Professional video processing with pan/zoom effects
- **Scale-to-Zero**: Automatic scaling from 0-10 instances based on demand
- **Cost-Effective**: ~$5-10/month vs ~$18/month for Function App B1
- **Error Handling**: Per-video error handling with detailed failure reporting
- **Health Check**: Built-in health endpoint for monitoring

## API Endpoints

### Health Check
```http
GET /health
```

### Generate Videos
```http
POST /api/generate
Content-Type: application/json

{
  "storageFolders": [
    "age-8/2024-01-13/article-0",
    "age-8/2024-01-13/article-1"
  ]
}
```

Each storage folder must contain:
- `image.png` - The article image
- `speech.mp3` - The audio narration
- `article.json` - Article metadata (optional, for title overlay)

## Video Specifications

- **Format**: MP4 (H.264 video, AAC audio)
- **Resolution**: 1080x1350 (portrait, 4:5 aspect ratio for Instagram)
- **Frame Rate**: 25 FPS
- **Duration**: Matched to audio length
- **Effects**: Random selection of:
  - Zoom in (1.0x → 1.5x)
  - Zoom out (1.5x → 1.0x)
  - Pan left to right
  - Pan right to left
- **Title Overlay**: Centered, white text with black background box (if title provided)

## Local Development

### Prerequisites
- Docker Desktop
- PowerShell
- Azurite (for local storage emulation)

### Quick Start

1. **Start Azurite** (in VS Code or terminal):
   ```powershell
   azurite
   ```

2. **Build and run the container**:
   ```powershell
   .\build-and-run.ps1 -Build -Run
   ```

3. **Test the API**:
   ```powershell
   # Test health endpoint
   Invoke-RestMethod -Uri "http://localhost:8080/health" -Method Get

   # Generate videos for test folders
   .\test-api.ps1 -StorageFolders "test-folder-1,test-folder-2"
   ```

4. **View logs**:
   ```powershell
   docker logs -f videogenerator-local
   ```

5. **Stop the container**:
   ```powershell
   .\build-and-run.ps1 -Stop
   ```

### VS Code Integration

Use the pre-configured launch configuration:
- **F5** with "Run VideoGenerator Container App" selected
- Or run task: `Ctrl+Shift+P` → "Run Task" → "docker build and run: VideoGenerator Container App"

## Azure Deployment

See [DEPLOYMENT.md](DEPLOYMENT.md) for detailed deployment instructions.

### Quick Deploy

```powershell
# One-time setup
cd scripts
.\setup-video-generator-aca.ps1

# Deploy updates via GitHub Actions
git add .
git commit -m "Update VideoGenerator"
git push origin master
```

## Project Structure

```
containers/VideoGenerator/
├── Program.cs              # Main API implementation
├── VideoGenerator.csproj   # Project file
├── Dockerfile             # Multi-stage Docker build
├── appsettings.json       # Configuration
├── build-and-run.ps1      # Local development script
├── test-api.ps1           # API testing script
├── DEPLOYMENT.md          # Deployment guide
└── README.md             # This file
```

## Configuration

### Environment Variables

- `AzureWebJobsStorage` - Azure Storage connection string (required)
- `BLOB_CONTAINER_NAME` - Blob container name (default: `batch-runs`)
- `ASPNETCORE_URLS` - Listening URLs (default: `http://+:8080`)

### Local Settings

For local development with Azurite:
```
AzureWebJobsStorage=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1;
BLOB_CONTAINER_NAME=batch-runs
```

## Architecture

### Request Flow

1. Client sends POST request with array of storage folders
2. API validates request and initializes batch processing
3. For each folder:
   - Download image, audio, and article JSON from blob storage
   - Extract audio duration using ffprobe
   - Generate video with FFMPEG (random pan/zoom effect)
   - Upload video back to blob storage
   - Track success/failure status
4. Return batch results with individual folder outcomes

### Error Handling

Errors are handled per-folder, not per-batch:
- Failed folders return `success: false` with error message
- Successful folders return `success: true` with video URL
- Batch continues even if individual folders fail
- HTTP 200 returned with mixed results (some success, some failure)
- HTTP 400 only for invalid requests (missing parameters)

### Performance

- **Sequential Processing**: Videos generated one at a time across ALL requests using a global semaphore
- **Concurrency Control**: Semaphore ensures only one FFMPEG process runs at a time to prevent OOM
- **Request Queuing**: Concurrent requests wait for their turn to process videos
- **Temporary Storage**: Uses `/tmp` for intermediate files, cleaned up after each video
- **Timeout**: Generous timeout (10 minutes) for batch operations
- **Memory**: 2GB per instance, optimized for FFMPEG video processing with reduced resolution scaling
- **FFMPEG Preset**: `faster` for improved processing speed (~30% faster than medium)

## Migration from Function App

This service replaces the legacy `lambdas/VideoGenerator` Azure Function:

**Key Differences:**
- **Architecture**: Function App B1 → Container App (scale-to-zero)
- **API**: Single folder → Batch processing
- **Cost**: $18/month → $5-10/month (45-70% reduction)
- **Scaling**: Manual (1-3) → Automatic (0-10)
- **Endpoint**: Changed from `/api/VideoGenerator` to `/api/generate`

**Migration Steps:**
1. Deploy Container App
2. Update orchestrator `VIDEO_GENERATOR_URL` environment variable
3. Test with new endpoint
4. Delete old Function App and App Service Plan

## Concurrency and Scaling

### How Concurrent Requests Are Handled

The VideoGenerator uses a **global semaphore** to ensure only one video generation happens at a time:

1. **Single Request, Multiple Folders**: Processes folders sequentially within the request
2. **Multiple Concurrent Requests**: Each request waits in line for the semaphore
3. **Why?** FFMPEG video processing is memory and CPU intensive - running multiple in parallel would cause OOM

### Example Scenario

```
Request A: Generate videos for [folder1, folder2, folder3]
Request B: Generate videos for [folder4, folder5] (arrives while A is processing)

Timeline:
1. Request A acquires semaphore, starts folder1
2. Request B waits for semaphore
3. Request A finishes folder1, processes folder2
4. Request B still waiting...
5. Request A finishes folder3, releases semaphore
6. Request B acquires semaphore, starts folder4
7. Request B finishes, releases semaphore
```

### Scaling with Azure Container Apps

- **min-replicas: 0** - Scales to zero when idle (cost savings)
- **max-replicas: 10** - Can scale up to 10 instances for parallel processing
- **Each instance** processes one video at a time (semaphore is per-instance)
- **HTTP scaling rule** - Azure automatically creates new instances when requests queue up

With 10 replicas, you can process **10 videos in parallel** (one per instance).

### If You Need Higher Throughput

Option 1: **Let Azure scale automatically** (recommended)
- Azure will create more instances as requests pile up
- Each instance handles one video at a time
- Cost-effective: only pay for active instances

Option 2: **Increase semaphore count** (not recommended)
```csharp
// Allow 2 concurrent videos per instance (requires 4GB memory)
var videoGenerationSemaphore = new SemaphoreSlim(2, 2);
```

Option 3: **Set min-replicas > 0** to avoid cold starts
```bash
az containerapp update --min-replicas 1 --max-replicas 10
```

## Monitoring and Logging

### Application Insights Integration

The VideoGenerator is configured with **Application Insights** for structured logging and monitoring.

**What gets tracked:**
- All `logger.LogInformation()`, `logger.LogWarning()`, `logger.LogError()` calls
- Request traces with duration and status
- Dependency tracking (Azure Storage calls)
- Exception tracking with full stack traces
- Custom metrics and telemetry

**Access logs in Azure Portal:**
1. Navigate to **Application Insights** → `ai-newspaper-app-insights`
2. Click **"Logs"** under Monitoring
3. Use KQL queries to filter logs:

```kusto
// All traces from VideoGenerator
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| order by timestamp desc
| take 100

// Only errors
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| where severityLevel >= 3  // Error level
| order by timestamp desc

// Search for specific folder processing
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| where message contains "age-8/2024-01-13/article-0"
| order by timestamp desc

// Video generation performance
requests
| where cloud_RoleName == "ai-newspaper-video-generator"
| where name == "POST /api/generate"
| summarize avg(duration), max(duration), count() by bin(timestamp, 1h)
```

**Access via CLI:**
```bash
# Container logs (console output)
az containerapp logs show \
  --name ai-newspaper-video-generator \
  --resource-group ai-newspaper-rg \
  --tail 100 \
  --follow
```

### Log Levels

- **Information**: Normal operations (video generation started, completed, etc.)
- **Warning**: Non-critical issues (missing article.json, blob not found)
- **Error**: Failures (FFMPEG errors, OOM issues, upload failures)

## Troubleshooting

### 504 Gateway Timeout from Orchestrator

If the NewspaperOrchestrator gets a 504 timeout after exactly 240 seconds, see **[TIMEOUT-ISSUES.md](TIMEOUT-ISSUES.md)** for detailed explanation and solutions.

**Applied optimizations:**
- ✅ FFMPEG preset changed to `faster` (~30% speed improvement)
- ✅ Resolution scaling reduced to 2x (memory and speed optimized)
- ✅ Thread limit set to 2 (prevents resource contention)
- ✅ Scale-to-zero enabled (cost-effective)

**Root cause:** Azure Container Apps has a hard 240-second ingress timeout that cannot be changed.

### Container won't start
- Check Docker is running: `docker ps`
- Check Azurite is accessible: `http://localhost:10000/devstoreaccount1`
- View container logs: `docker logs videogenerator-local`

### FFMPEG errors
- Ensure image is PNG format
- Ensure audio is MP3 format
- Check audio duration is valid (>0 seconds)
- Verify files exist in blob storage

### Storage access errors
- Verify Azurite is running (local)
- Check connection string is correct
- Ensure blob container exists
- Confirm files exist at expected paths

### Cold start in Azure
- First request after idle may take 10-30 seconds
- This is normal for scale-to-zero architecture
- Consider setting min replicas to 1 if unacceptable

## Performance Tuning

### Local Development
- Reduce video resolution for faster testing
- Use shorter audio files
- Disable title overlay

### Azure Production
- Increase CPU/Memory if videos timeout
- Adjust max replicas for higher concurrency
- Add custom scale rules for queue-based scaling
- Consider setting min replicas >0 to avoid cold starts

## License

Part of the AI Newspaper project.
