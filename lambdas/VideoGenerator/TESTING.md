# VideoGenerator Testing Guide

This guide will help you test the VideoGenerator function locally.

## Prerequisites

1. **Docker Desktop** installed and running
2. **Azurite** (Azure Storage Emulator) running
3. Test files ready: `image.png`, `speech.mp3`, and optionally `article.json`

## Quick Start

### Step 1: Build the Docker Image

```powershell
cd lambdas\VideoGenerator
.\build-and-run.ps1
```

Or manually:
```powershell
docker build -t videogenerator:local .
```

### Step 2: Run the Container

```powershell
.\build-and-run.ps1 -Run
```

Or manually:
```powershell
docker run -p 7076:80 `
  -e AzureWebJobsStorage="UseDevelopmentStorage=true" `
  -e BLOB_CONTAINER_NAME="batch-runs" `
  videogenerator:local
```

### Step 3: Prepare Test Data

You need to upload test files to Azure Blob Storage (Azurite). Here's how:

#### Option A: Using Azure Storage Explorer
1. Open Azure Storage Explorer
2. Connect to Local Storage Emulator (Azurite)
3. Navigate to Blob Containers
4. Create container named `batch-runs` if it doesn't exist
5. Create a folder (e.g., `test-20240101`)
6. Upload these files to the folder:
   - `image.png` - Any image file (portrait or square format works best; will be cropped to 1080x1350 for Instagram)
   - `speech.mp3` - An MP3 audio file (1-3 minutes recommended)
   - `article.json` - JSON file with article metadata (optional)

Example `article.json` (matches the structure from NewspaperOrchestrator):
```json
{
  "url": "https://example.com/news/article",
  "title": "Breaking News: Scientists Discover New Species",
  "simplifiedArticle": "Scientists have discovered a new species of butterfly in the Amazon rainforest...",
  "imageUrl": "https://storage.example.com/batch-runs/test/image.png",
  "imageDescription": "A beautiful blue and green butterfly",
  "audioUrl": "https://storage.example.com/batch-runs/test/speech.mp3"
}
```

#### Option B: Using Azure CLI
```powershell
# Set connection string for Azurite
$env:AZURE_STORAGE_CONNECTION_STRING = "UseDevelopmentStorage=true"

# Create container
az storage container create --name batch-runs

# Upload files
az storage blob upload --container-name batch-runs --name test-20240101/image.png --file path\to\image.png
az storage blob upload --container-name batch-runs --name test-20240101/speech.mp3 --file path\to\speech.mp3
az storage blob upload --container-name batch-runs --name test-20240101/article.json --file path\to\article.json
```

### Step 4: Test the Function

```powershell
.\test-function.ps1 -StorageFolder "test-20240101"
```

Or manually with curl:
```powershell
curl -X POST http://localhost:7076/api/VideoGenerator `
  -H "Content-Type: application/json" `
  -d '{"storageFolder":"test-20240101"}'
```

Or with Invoke-RestMethod:
```powershell
$body = @{ storageFolder = "test-20240101" } | ConvertTo-Json
Invoke-RestMethod -Uri http://localhost:7076/api/VideoGenerator -Method Post -Body $body -ContentType "application/json"
```

### Step 5: Verify the Output

After the function completes, check your storage:
1. Navigate to the same folder in Azure Storage Explorer
2. You should see a new file: `video.mp4`
3. Download and play the video to verify it has:
   - Portrait format (1080x1350) for Instagram
   - Slow zoom/pan effect on the image
   - Audio track synced with video
   - Article title overlay (if article.json was provided)

## Expected Response

```json
{
  "storageFolder": "test-20240101",
  "videoUrl": "http://127.0.0.1:10000/devstoreaccount1/batch-runs/test-20240101/video.mp4"
}
```

## Testing Without Docker (Alternative)

If you prefer to test without Docker, you can run the function directly with the Azure Functions Core Tools, but you need FFMPEG installed:

### Install FFMPEG

**Windows (using Chocolatey):**
```powershell
choco install ffmpeg
```

**Windows (manual):**
1. Download from https://ffmpeg.org/download.html
2. Extract to `C:\ffmpeg`
3. Add `C:\ffmpeg\bin` to your PATH environment variable

**Verify installation:**
```powershell
ffmpeg -version
ffprobe -version
```

### Run the Function

```powershell
cd lambdas\VideoGenerator
func start
```

The function will be available at `http://localhost:7076/api/VideoGenerator`

## Troubleshooting

### "Docker command not found"
- Install Docker Desktop: https://www.docker.com/products/docker-desktop
- Make sure Docker Desktop is running

### "Connection refused" or "Storage emulator not found"
- Install Azurite: `npm install -g azurite`
- Start Azurite: `azurite --silent --location c:\azurite --debug c:\azurite\debug.log`
- Or use Azure Storage Emulator (legacy)

### "Blob not found" error
- Verify the storage folder path is correct
- Check that files are uploaded to the correct container (`batch-runs`)
- Make sure Azurite is running and accessible

### "FFMPEG not found" (when running without Docker)
- Install FFMPEG and ensure it's in your PATH
- Run `ffmpeg -version` to verify installation

### Video generation takes too long
- Video processing time is approximately 0.3-0.5x the audio duration
- For a 2-minute audio file, expect 40-60 seconds of processing time
- Check function logs for detailed progress

### Poor video quality
- Adjust the FFMPEG parameters in `VideoGeneratorFunction.cs`:
  - Change `-crf 23` to a lower value (e.g., 18) for higher quality
  - Change `-preset medium` to `slow` or `slower` for better compression
  - Adjust `-b:a 192k` for audio bitrate

## Advanced Testing

### Test with Different Image Sizes
- Try images with different aspect ratios
- The function will scale/crop to 1080x1350 (Instagram portrait format)

### Test with Long Audio
- Test with audio files of different lengths (30 seconds to 5 minutes)
- Monitor memory usage and processing time

### Test Without Article Title
- Don't upload `article.json` to test video generation without text overlay

### Test Error Handling
- Provide invalid storage folder (should return appropriate error)
- Provide empty storage folder (should fail gracefully)
- Test with corrupted image or audio files

## Performance Benchmarks

Expected performance on standard hardware:

| Audio Duration | Processing Time | Output Size |
|---------------|-----------------|-------------|
| 30 seconds    | 15-20 seconds   | ~2-5 MB     |
| 1 minute      | 25-35 seconds   | ~5-10 MB    |
| 2 minutes     | 40-60 seconds   | ~10-20 MB   |
| 3 minutes     | 60-90 seconds   | ~15-30 MB   |

## Next Steps

Once local testing is successful:
1. Deploy to Azure Functions (Premium or Dedicated plan with container support)
2. Integrate with NewspaperOrchestrator
3. Test end-to-end workflow with real articles
