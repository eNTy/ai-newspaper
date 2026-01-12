# VideoGenerator Azure Function

This Azure Function generates portrait-format videos (1080x1350) optimized for Instagram from images and audio using FFMPEG. It combines a static image with dynamic pan/zoom effects, an audio track, and an optional text overlay.

## ⚠️ Important: Docker Required

**This function requires Docker to run.** Unlike other functions in this project, VideoGenerator depends on FFMPEG, which must be installed in a container. You cannot run it with `func start` or `dotnet run` alone (unless you have FFMPEG installed locally).

See [VSCODE-DEBUG.md](VSCODE-DEBUG.md) for VS Code debugging instructions.

## Features

- **Dynamic Pan/Zoom Effect**: Slowly zooms into the image while panning to create visual interest
- **Audio Synchronization**: Uses the provided MP3 file as the audio track, matching video duration to audio length
- **Text Overlay**: Optionally displays the article title as an overlay on the video
- **Azure Blob Storage Integration**: Reads input files and writes output to Azure Blob Storage

## Input

The function expects a folder in Azure Blob Storage containing:
- `image.png` - The image to use in the video
- `speech.mp3` - The audio track
- `article.json` (optional) - JSON file containing article metadata with `ArticleTitle` field

## Output

The function generates:
- `video.mp4` - Output video file in the same folder

## Quick Start

**For the fastest way to get started, see [QUICK-START.md](QUICK-START.md)**

```powershell
# Build and run in one command
.\build-and-run.ps1 -Run

# Or quick restart (uses cached image, ~5 seconds)
.\build-and-run.ps1 -Run -NoBuild
```

## Local Development

### Prerequisites

1. **.NET 8.0 SDK** installed
2. **Docker Desktop** installed and running
3. **Azure Storage Emulator (Azurite)** running
4. **Visual Studio 2022** or **VS Code** with Azure Functions extension

### Building the Docker Image

```bash
cd lambdas/VideoGenerator
docker build -t videogenerator:local .
```

Or use the PowerShell script:
```powershell
.\build-and-run.ps1
```

### Running with Docker

```bash
docker run -p 7076:80 \
  -e AzureWebJobsStorage="UseDevelopmentStorage=true" \
  -e BLOB_CONTAINER_NAME="batch-runs" \
  videogenerator:local
```

### Testing Locally (without Docker)

If you want to test without Docker, you need to install FFMPEG locally:

**Windows:**
```bash
# Using Chocolatey
choco install ffmpeg

# Or download from https://ffmpeg.org/download.html
```

**macOS:**
```bash
brew install ffmpeg
```

**Linux:**
```bash
sudo apt-get update
sudo apt-get install ffmpeg
```

Then run:
```bash
cd lambdas/VideoGenerator
func start
```

### Sample Request

The function uses `AuthorizationLevel.Anonymous` for easy local development (no auth key required).

```json
POST http://localhost:7076/api/VideoGenerator
Content-Type: application/json

{
  "storageFolder": "batch-runs/20240101-120000/article-1"
}
```

Or use the test script:
```powershell
.\test-function.ps1 -StorageFolder "batch-runs/20240101-120000/article-1"
```

### Sample Response

```json
{
  "storageFolder": "batch-runs/20240101-120000/article-1",
  "videoUrl": "https://yourstorageaccount.blob.core.windows.net/batch-runs/batch-runs/20240101-120000/article-1/video.mp4"
}
```

## FFMPEG Command Explanation

The function uses the following FFMPEG operations:

1. **Scale and Crop**: Ensures the image fills a 1080x1350 portrait frame (Instagram format)
2. **Zoompan Filter**: Creates a slow zoom effect from 1.0x to 1.5x over the duration of the audio
3. **Drawtext Filter** (optional): Overlays the article title at the top center with a semi-transparent black background
4. **Audio Mapping**: Syncs the audio track with the video
5. **Encoding**: Uses H.264 video codec and AAC audio codec for maximum compatibility

## Configuration

Environment variables (set in `local.settings.json` for local development):

- `AzureWebJobsStorage`: Connection string for Azure Storage
- `BLOB_CONTAINER_NAME`: Name of the blob container (e.g., "batch-runs")
- `FUNCTIONS_WORKER_RUNTIME`: "dotnet-isolated"

## Deployment

### Deploy to Azure Functions (Container)

1. Create an Azure Container Registry (ACR):
```bash
az acr create --name yourregistry --resource-group yourgroup --sku Basic
```

2. Build and push the image:
```bash
az acr build --registry yourregistry --image videogenerator:v1 .
```

3. Create Azure Function App (Premium or Dedicated plan):
```bash
az functionapp create \
  --name videogenerator-func \
  --storage-account yourstorage \
  --resource-group yourgroup \
  --plan yourplan \
  --deployment-container-image-name yourregistry.azurecr.io/videogenerator:v1
```

4. Configure app settings:
```bash
az functionapp config appsettings set \
  --name videogenerator-func \
  --resource-group yourgroup \
  --settings \
    AzureWebJobsStorage="<connection-string>" \
    BLOB_CONTAINER_NAME="batch-runs"
```

## Troubleshooting

### FFMPEG not found
- Ensure FFMPEG is installed in the Docker container (check Dockerfile)
- For local development, install FFMPEG on your system

### Video generation fails
- Check that input files exist in the specified storage folder
- Verify audio file is a valid MP3
- Check function logs for FFMPEG error messages

### Large video files / Long processing time
- Video processing time depends on audio duration
- Typical 2-3 minute audio takes 30-60 seconds to process
- Consider Lambda execution time limits (15 minutes max)

### Video has wrong aspect ratio
- The function generates 1080x1350 (4:5 aspect ratio) for Instagram portrait format
- Images are automatically scaled and cropped to fit this format

## Performance Considerations

- **Cold Start**: Container-based functions have longer cold starts (5-10 seconds)
- **Processing Time**: Approximately 0.3-0.5x real-time (2 minute audio = 40-60 seconds processing)
- **Memory Usage**: Recommend at least 1GB memory allocation
- **Temp Storage**: Each video generation uses ~50-200MB of temp storage

## License

Part of the ai-newspaper project.
