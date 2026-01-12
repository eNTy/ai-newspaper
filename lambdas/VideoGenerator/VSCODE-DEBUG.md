# VS Code Debugging Guide for VideoGenerator

## ⚠️ Important: Docker Required

Unlike other functions in this project, **VideoGenerator CANNOT be run with `func start` or `dotnet run`** directly. It requires FFMPEG, which is only available inside the Docker container.

## VS Code Tasks Available

### 1. Build Task (Code Validation Only)
- **Task**: `build VideoGenerator`
- **Purpose**: Validates C# code compiles correctly
- **Does NOT**: Run the function or include FFMPEG
- **Use When**: Checking for compilation errors

```
Ctrl+Shift+B → Select "build VideoGenerator"
```

### 2. Docker Build Task
- **Task**: `docker build: VideoGenerator`
- **Purpose**: Builds the Docker image with FFMPEG included
- **Runtime**: 5-10 minutes first time, 1-2 minutes after
- **Result**: Creates `videogenerator:local` image

```
Ctrl+Shift+P → Tasks: Run Task → "docker build: VideoGenerator"
```

### 3. Docker Run Task
- **Task**: `docker run: VideoGenerator`
- **Purpose**: Starts the container on port 7076
- **Prerequisites**:
  - Docker image built (see task #2)
  - Azurite running (automatically started)
- **Background**: Runs as background task
- **Access**: http://localhost:7076/api/VideoGenerator

```
Ctrl+Shift+P → Tasks: Run Task → "docker run: VideoGenerator"
```

## Debugging Workflow

### Option 1: Manual Docker Debugging (Recommended)

1. **Build the Docker image**:
   ```powershell
   cd lambdas\VideoGenerator
   docker build -t videogenerator:local .
   ```

2. **Run container with debugging**:
   ```powershell
   docker run --rm -p 7076:80 `
     -e AzureWebJobsStorage="UseDevelopmentStorage=true" `
     -e BLOB_CONTAINER_NAME="batch-runs" `
     videogenerator:local
   ```

3. **View logs in terminal** to see function output

4. **Test with PowerShell**:
   ```powershell
   .\test-function.ps1 -StorageFolder "your-test-folder"
   ```

### Option 2: VS Code Tasks

1. **Start Azurite** (if not already running):
   ```
   Ctrl+Shift+P → Tasks: Run Task → "start azurite"
   ```

2. **Build Docker image**:
   ```
   Ctrl+Shift+P → Tasks: Run Task → "docker build: VideoGenerator"
   ```

3. **Run container**:
   ```
   Ctrl+Shift+P → Tasks: Run Task → "docker run: VideoGenerator"
   ```

4. **Test the function**:
   - Use test-function.ps1 script
   - Or use REST client/Postman
   - Or use curl

### Option 3: Local Development (FFMPEG Required)

Only if you have FFMPEG installed locally:

**Install FFMPEG**:
```powershell
choco install ffmpeg
```

**Run function**:
```powershell
cd lambdas\VideoGenerator
func start --port 7076
```

**Debug**:
- Set breakpoints in VideoGeneratorFunction.cs
- Press F5 in VS Code
- Select "Attach to .NET Functions"
- Pick the func.exe process

## Why No Standard Debug Configuration?

The solution-level `.vscode/launch.json` does **NOT** include VideoGenerator because:

1. **FFMPEG Dependency**: Function requires FFMPEG binary not available on host
2. **Container Only**: Must run in Docker for proper environment
3. **Different Workflow**: Uses `docker run` instead of `func start`
4. **Complexity**: Would require complex VS Code Docker extension setup

## Attaching Debugger to Container (Advanced)

If you need to debug inside the running container:

1. **Run container with debug port exposed**:
   ```powershell
   docker run --rm -p 7076:80 -p 5000:5000 `
     -e AzureWebJobsStorage="UseDevelopmentStorage=true" `
     -e BLOB_CONTAINER_NAME="batch-runs" `
     videogenerator:local
   ```

2. **Attach to remote process**:
   - This requires additional Docker configuration
   - Not recommended for normal development
   - Better to rely on logging

## Testing Strategy

Since traditional debugging is limited, use these strategies:

### 1. Comprehensive Logging
The function already has extensive logging:
```csharp
_logger.LogInformation("Downloading files from Azure Storage...");
_logger.LogInformation($"Audio duration: {audioDuration} seconds");
_logger.LogInformation($"FFMPEG command: ffmpeg {arguments}");
```

Watch Docker container output for these logs.

### 2. Test with Known Good Data
Use sample files that work:
- Simple PNG image
- Short MP3 file (30-60 seconds)
- Valid article.json

### 3. Incremental Testing
Test each component:
1. File download from storage ✓
2. Audio duration detection ✓
3. FFMPEG execution ✓
4. Video upload to storage ✓

### 4. Error Inspection
Check logs for:
- FFMPEG stderr output
- Azure Storage errors
- File I/O issues

## Quick Reference

| Task | Command | Time | Purpose |
|------|---------|------|---------|
| Build C# code | `dotnet build` | 5s | Validate code |
| Build Docker image | `docker build -t videogenerator:local .` | 5-10min | Create container |
| Run container | `docker run -p 7076:80 ...` | 5s | Start function |
| Test function | `.\test-function.ps1` | 30-90s | Generate video |
| View logs | `docker logs videogen` | 1s | Check output |
| Stop container | `docker stop videogen` | 1s | Stop function |

## Troubleshooting

### "Docker is not available"
- Install Docker Desktop
- Ensure Docker daemon is running

### "Container won't start"
- Check if port 7076 is already in use
- Verify Azurite is running
- Check Docker Desktop resources

### "Function returns 500 error"
- Check container logs: `docker logs videogen`
- Verify test files exist in storage
- Ensure FFMPEG is installed in container: `docker run --rm videogenerator:local ffmpeg -version`

### "Cannot attach debugger"
- VideoGenerator uses container, not local func.exe
- Use logging instead of breakpoints
- Or install FFMPEG locally and run with `func start`

## Comparison with Other Functions

| Function | Debug Method | Why |
|----------|-------------|-----|
| ImageGenerator | `func start` + F5 | Pure C# + OpenAI API |
| TextToSpeech | `func start` + F5 | Pure C# + OpenAI API |
| ArticleSimplifier | `func start` + F5 | Pure C# + OpenAI API |
| VideoGenerator | **Docker only** | **Requires FFMPEG binary** |

## Summary

- ✅ Build task validates code
- ✅ Docker tasks run the function
- ❌ No F5 debugging (use logging)
- ✅ Test with scripts
- ✅ Monitor container logs

For most development, this workflow works well:
1. Edit code in VS Code
2. Build Docker image
3. Run container
4. Test with script
5. Check logs for issues
6. Repeat

**Note**: Once deployed to Azure, the function works like any other Azure Function - this complexity is only for local development.
