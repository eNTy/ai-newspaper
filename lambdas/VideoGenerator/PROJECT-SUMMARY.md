# VideoGenerator Project Summary

## Overview

The VideoGenerator is an Azure Function that creates portrait-format videos (1080x1350) optimized for Instagram. It combines images with dynamic pan/zoom effects, syncs audio narration, and overlays article titles.

## Project Status: ✅ Ready for Testing

- **Build Status**: ✅ All builds pass (no errors or warnings)
- **Docker Support**: ✅ Dockerfile created and validated
- **Documentation**: ✅ Comprehensive guides provided
- **Integration**: ✅ Compatible with NewspaperOrchestrator JSON structure
- **Testing Scripts**: ✅ PowerShell scripts for local testing ready

## Key Features

1. **Instagram-Optimized Output**
   - Portrait format: 1080x1350 (4:5 aspect ratio)
   - Ideal for Instagram feed, Reels, and Stories

2. **Dynamic Visual Effects**
   - Slow zoom effect (1.0x to 1.5x over audio duration)
   - Center-focused panning
   - Smooth 25 fps output

3. **Audio Synchronization**
   - Video duration automatically matches audio length
   - High-quality AAC audio encoding (192kbps)

4. **Text Overlay**
   - Article title displayed at top with semi-transparent background
   - Font: DejaVu Sans Bold, 40px
   - Positioned at y=80 for optimal visibility

5. **Azure Integration**
   - Reads input files from Azure Blob Storage
   - Uploads output video to same folder
   - Compatible with orchestrator's file structure

## Architecture

```
Input:
  └── Azure Storage Folder
      ├── image.png       (any aspect ratio, will be cropped)
      ├── speech.mp3      (audio narration)
      └── article.json    (metadata with title, optional)

Processing:
  ├── Download files to temp directory
  ├── Extract article title from JSON
  ├── Get audio duration with ffprobe
  ├── Generate video with FFMPEG
  │   ├── Scale/crop image to 1080x1350
  │   ├── Apply zoom/pan effect
  │   ├── Overlay article title (if available)
  │   └── Sync with audio track
  └── Upload video.mp4 to storage

Output:
  └── Azure Storage Folder
      └── video.mp4       (1080x1350, H.264/AAC, MP4)
```

## File Structure

```
lambdas/VideoGenerator/
├── VideoGeneratorFunction.cs     # Main function implementation
├── Program.cs                    # Entry point
├── VideoGenerator.csproj         # Project configuration
├── host.json                     # Function host settings
├── local.settings.json           # Local development config (port 7076)
├── Dockerfile                    # Container definition with FFMPEG
├── .dockerignore                 # Docker build exclusions
├── README.md                     # Comprehensive documentation
├── TESTING.md                    # Detailed testing guide
├── DOCKER-BUILD-GUIDE.md        # Docker build instructions
├── PROJECT-SUMMARY.md           # This file
├── sample-article.json          # Sample input data
├── test-request.json            # Sample HTTP request
├── build-and-run.ps1            # Build and run container
├── test-docker-build.ps1        # Test Docker build
├── test-function.ps1            # Test function endpoint
└── .vscode/
    ├── launch.json              # Debug configuration
    └── tasks.json               # Build tasks
```

## Technology Stack

- **.NET 8.0**: Target framework
- **Azure Functions v4**: Isolated worker model
- **FFMPEG**: Video processing (installed in Docker container)
- **Azure Blob Storage**: Input/output file storage
- **Docker**: Containerized deployment

## Dependencies

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.21.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.1.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.17.0" />
<PackageReference Include="Azure.Storage.Blobs" Version="12.19.1" />
```

## Configuration

### Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `AzureWebJobsStorage` | Storage connection string | `UseDevelopmentStorage=true` (local) |
| `BLOB_CONTAINER_NAME` | Container for input/output | `batch-runs` |
| `FUNCTIONS_WORKER_RUNTIME` | Runtime type | `dotnet-isolated` |

### Local Settings

- **Port**: 7076 (HTTP endpoint)
- **CORS**: Enabled (`*` in local development)
- **Host**: Azure Functions isolated worker

## JSON Data Contract

### Input: article.json (Optional)

```json
{
  "url": "string",              // Article source URL
  "title": "string",            // Article title (used for overlay)
  "simplifiedArticle": "string", // Simplified content
  "imageUrl": "string",         // URL to image.png in storage
  "imageDescription": "string",  // AI-generated description
  "audioUrl": "string"          // URL to speech.mp3 in storage
}
```

**Note**: Matches `ProcessedArticle` model from NewspaperOrchestrator with camelCase serialization.

### HTTP Request

```json
{
  "storageFolder": "batch-runs/20240101-120000/article-1"
}
```

### HTTP Response

```json
{
  "storageFolder": "batch-runs/20240101-120000/article-1",
  "videoUrl": "https://account.blob.core.windows.net/batch-runs/.../video.mp4"
}
```

## FFMPEG Command

The function generates the following FFMPEG command:

```bash
ffmpeg -loop 1 -i "image.png" -i "speech.mp3" \
  -filter_complex "[0:v]scale=1080:1350:force_original_aspect_ratio=increase,crop=1080:1350,\
  zoompan=z='min(zoom+0.0015,1.5)':d={duration*25}:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s=1080x1350:fps=25[zoomed];\
  [zoomed]drawtext=fontfile=/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf:\
  text='Article Title':fontcolor=white:fontsize=40:box=1:boxcolor=black@0.7:boxborderw=10:\
  x=(w-text_w)/2:y=80[final]" \
  -map "[final]" -map 1:a \
  -c:v libx264 -preset medium -crf 23 \
  -c:a aac -b:a 192k \
  -shortest -pix_fmt yuv420p -movflags +faststart \
  "video.mp4"
```

### FFMPEG Parameters Explained

- **-loop 1**: Loop the input image
- **scale/crop**: Resize to 1080x1350 maintaining aspect ratio
- **zoompan**: Smooth zoom from 1.0x to 1.5x, center-focused
- **drawtext**: Overlay title with background box
- **libx264**: H.264 video codec for compatibility
- **crf 23**: Quality level (lower = better quality, larger file)
- **preset medium**: Encoding speed vs compression tradeoff
- **aac -b:a 192k**: AAC audio at 192kbps bitrate
- **-shortest**: Match video length to audio length
- **yuv420p**: Pixel format for maximum compatibility
- **+faststart**: Optimize for web streaming

## Performance Characteristics

### Processing Time
| Audio Length | Expected Processing Time |
|--------------|-------------------------|
| 30 seconds   | 15-20 seconds          |
| 1 minute     | 25-35 seconds          |
| 2 minutes    | 40-60 seconds          |
| 3 minutes    | 60-90 seconds          |
| 5 minutes    | 100-150 seconds        |

**Formula**: Approximately 0.3-0.5x real-time

### Resource Usage
- **Memory**: 512 MB - 1 GB (recommend 1 GB minimum)
- **Temp Storage**: 50-200 MB per video
- **CPU**: Benefits from multi-core (FFMPEG is multi-threaded)

### Output File Size
| Video Length | Approximate Size |
|--------------|-----------------|
| 30 seconds   | 2-5 MB         |
| 1 minute     | 5-10 MB        |
| 2 minutes    | 10-20 MB       |
| 3 minutes    | 15-30 MB       |
| 5 minutes    | 25-50 MB       |

### Azure Functions Limits
- **Execution Time**: 15 minutes max (Azure Functions hard limit)
- **Temp Storage**: 10 GB available in `/tmp` or `D:\local\Temp`
- **Memory**: Depends on plan (Premium/Dedicated recommended)

## Testing Workflow

### 1. Local Build Test (No Docker)
```powershell
cd lambdas\VideoGenerator
dotnet build
dotnet run
# Requires FFMPEG installed locally
```

### 2. Docker Build Test
```powershell
cd lambdas\VideoGenerator
.\test-docker-build.ps1
# Tests build and FFMPEG installation
```

### 3. Container Run Test
```powershell
.\build-and-run.ps1 -Run
# Starts container on port 7076
```

### 4. Function Endpoint Test
```powershell
.\test-function.ps1 -StorageFolder "test-folder"
# Calls function with test data
```

### 5. Output Verification
- Download `video.mp4` from storage
- Verify resolution: 1080x1350
- Check zoom/pan effect
- Verify audio sync
- Confirm title overlay (if article.json provided)

## Integration with NewspaperOrchestrator

### Current State
- ✅ JSON structure matches `ProcessedArticle` model
- ✅ File naming conventions compatible
- ✅ Storage folder structure aligned
- ⏳ Not yet integrated into orchestrator workflow

### Integration Steps Required

1. **Add VideoGenerator Request/Response to Models.cs**
   ```csharp
   public class VideoGeneratorRequest
   {
       public string StorageFolder { get; set; } = string.Empty;
   }

   public class VideoGeneratorResponse
   {
       public string VideoUrl { get; set; } = string.Empty;
   }
   ```

2. **Add VideoUrl to ProcessedArticle**
   ```csharp
   public class ProcessedArticle
   {
       // ... existing properties ...
       public string VideoUrl { get; set; } = string.Empty;
   }
   ```

3. **Add GenerateVideo Activity to Orchestrator**
   ```csharp
   [Function(nameof(GenerateVideo))]
   public async Task<VideoGeneratorResponse> GenerateVideo(
       [ActivityTrigger] VideoGeneratorRequest request,
       FunctionContext context)
   {
       // Call VideoGenerator HTTP endpoint
   }
   ```

4. **Update Orchestration Flow**
   - After `GenerateImage` and `GenerateAudio` complete
   - Call `GenerateVideo` activity
   - Update `ProcessedArticle` with `VideoUrl`

5. **Add VIDEO_GENERATOR_URL Environment Variable**
   ```json
   "VIDEO_GENERATOR_URL": "http://localhost:7076/api/VideoGenerator"
   ```

## Deployment Options

### Option 1: Azure Container Registry + Azure Functions
1. Build and push to ACR
2. Create Function App with container image
3. Configure app settings
4. Enable Premium/Dedicated plan (required for containers)

### Option 2: Direct Deployment (Without Container)
- **Not Recommended**: FFMPEG not available by default
- Would require custom deployment with FFMPEG binaries
- Container approach is cleaner and more reliable

### Option 3: Azure Container Instances
- Alternative if Function execution time is too limiting
- More flexible but requires different orchestration approach

## Known Limitations

1. **Container Only**: Function requires Docker container due to FFMPEG dependency
2. **Premium/Dedicated Plan Required**: Container support not available in Consumption plan
3. **Cold Start**: Container-based functions have longer cold starts (5-10 seconds)
4. **Processing Time**: Real-time processing not possible (0.3-0.5x real-time)
5. **Azure Functions Timeout**: 15-minute max execution (limits video length to ~30-40 minutes of audio)

## Future Enhancements

### Potential Improvements
- [ ] Support for multiple images (slideshow effect)
- [ ] Customizable zoom parameters
- [ ] Different aspect ratios (16:9, 9:16, 1:1)
- [ ] Background music support
- [ ] Multiple text overlays (subtitles)
- [ ] Transition effects between segments
- [ ] Watermark overlay
- [ ] Quality presets (low/medium/high)
- [ ] Progress tracking for long videos
- [ ] Thumbnail generation

### Performance Optimizations
- [ ] Hardware acceleration (if available in Azure)
- [ ] Parallel processing for batch operations
- [ ] Pre-warmed instances to reduce cold starts
- [ ] Optimized FFMPEG flags for faster encoding

## Troubleshooting Guide

See [DOCKER-BUILD-GUIDE.md](DOCKER-BUILD-GUIDE.md) for detailed troubleshooting steps.

Common issues:
- Docker not found → Install Docker Desktop
- Build fails → Check internet connection, try `--no-cache`
- Container won't start → Check Docker daemon, logs
- FFMPEG not found → Rebuild image without cache
- Function fails → Check environment variables, Azurite running

## Documentation Files

- **[README.md](README.md)**: User-facing documentation
- **[TESTING.md](TESTING.md)**: Detailed testing procedures
- **[DOCKER-BUILD-GUIDE.md](DOCKER-BUILD-GUIDE.md)**: Docker build instructions and troubleshooting
- **[PROJECT-SUMMARY.md](PROJECT-SUMMARY.md)**: This file - technical overview

## Contact & Support

For issues or questions:
1. Check documentation files first
2. Review function logs for errors
3. Verify Docker and FFMPEG installation
4. Check Azure Storage connectivity

## Version History

- **v1.0** (2024-01-12): Initial implementation
  - Portrait video generation (1080x1350)
  - FFMPEG-based processing
  - Docker container support
  - Azure Blob Storage integration
  - Text overlay support
  - Compatible with NewspaperOrchestrator JSON structure
