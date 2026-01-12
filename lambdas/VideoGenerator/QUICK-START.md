# VideoGenerator Quick Start Guide

## TL;DR - Fast Commands

```powershell
# First time: Build and run
.\build-and-run.ps1 -Run

# After that: Run without rebuilding (much faster!)
.\build-and-run.ps1 -Run -NoBuild

# View logs
.\build-and-run.ps1 -Logs

# Stop container
.\build-and-run.ps1 -Stop

# Clean everything
.\build-and-run.ps1 -Clean
```

## Complete Workflow

### 1. Initial Setup (One Time)

```powershell
cd lambdas\VideoGenerator

# Build Docker image (takes 5-10 minutes first time)
.\build-and-run.ps1
```

### 2. Daily Development Workflow

```powershell
# Start the function (uses existing image, starts in ~5 seconds)
.\build-and-run.ps1 -Run -NoBuild

# In another terminal, test it
.\test-function.ps1 -StorageFolder "test-folder"

# View logs if needed
.\build-and-run.ps1 -Logs

# When done, stop it
.\build-and-run.ps1 -Stop
```

### 3. After Code Changes

```powershell
# Stop existing container
.\build-and-run.ps1 -Stop

# Rebuild and run (takes 1-2 minutes with cache)
.\build-and-run.ps1 -Run
```

## All Available Commands

| Command | What It Does | Time |
|---------|--------------|------|
| `.\build-and-run.ps1` | Build image only | 5-10 min (first), 1-2 min (after) |
| `.\build-and-run.ps1 -Run` | Build and run | 5-10 min (first), 1-2 min (after) |
| `.\build-and-run.ps1 -Run -NoBuild` | Run without building | 5 sec |
| `.\build-and-run.ps1 -Logs` | View container logs | Instant |
| `.\build-and-run.ps1 -Stop` | Stop container | Instant |
| `.\build-and-run.ps1 -Clean` | Remove everything | Instant |

## Common Scenarios

### Scenario 1: First Time Setup
```powershell
# 1. Build the image
.\build-and-run.ps1

# 2. Run it
.\build-and-run.ps1 -Run -NoBuild

# 3. Test it
.\test-function.ps1 -StorageFolder "test-20240101"
```

### Scenario 2: Quick Restart After Code Change
```powershell
# Stop, rebuild, and run in one command
.\build-and-run.ps1 -Stop
.\build-and-run.ps1 -Run
```

### Scenario 3: Just Want to Test Without Changes
```powershell
# Start without rebuilding
.\build-and-run.ps1 -Run -NoBuild

# Test
.\test-function.ps1 -StorageFolder "test-20240101"

# Stop when done
.\build-and-run.ps1 -Stop
```

### Scenario 4: Debugging Issues
```powershell
# Start container
.\build-and-run.ps1 -Run -NoBuild

# In another terminal, watch logs
.\build-and-run.ps1 -Logs

# In third terminal, run tests
.\test-function.ps1 -StorageFolder "test-20240101"

# Logs will show real-time output
```

### Scenario 5: Clean Slate
```powershell
# Remove everything and start fresh
.\build-and-run.ps1 -Clean
.\build-and-run.ps1 -Run
```

## Prerequisites

Before running any commands:

1. **Docker Desktop** must be running
2. **Azurite** installed (will be started automatically)
   ```powershell
   # If not installed:
   npm install -g azurite
   ```

   The script will automatically start Azurite if it's not running.

## Important: Docker Network Configuration

When running in Docker, the container uses `host.docker.internal` to access Azurite on your host machine. The `build-and-run.ps1` script handles this automatically. If running Docker manually, use:

```powershell
# Note: host.docker.internal instead of 127.0.0.1
-e AzureWebJobsStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1;..."
```

## Troubleshooting

### "401 Unauthorized" error
The function is configured with `AuthorizationLevel.Anonymous` for local development, so no auth key is needed. If you still get 401:
```powershell
# Make sure you rebuilt after pulling latest code
.\build-and-run.ps1 -Run

# Or use the test script which handles auth
.\test-function.ps1 -StorageFolder "test"
```

### "Docker is not available"
```powershell
# Start Docker Desktop from Windows menu
# Wait for it to fully start, then try again
```

### "Port 7076 already in use"
```powershell
# Stop existing container
.\build-and-run.ps1 -Stop

# Or find and kill the process
netstat -ano | findstr :7076
```

### "Build fails"
```powershell
# Clean and rebuild
.\build-and-run.ps1 -Clean
.\build-and-run.ps1
```

### "Container starts but function doesn't work"
```powershell
# Check logs for errors
.\build-and-run.ps1 -Logs

# Common issues:
# - Azurite not running
# - Test files don't exist in storage
# - FFMPEG error (check logs)
```

### "Can't connect to storage"
```powershell
# Make sure Azurite is running
azurite

# In another terminal
.\build-and-run.ps1 -Run -NoBuild
```

## Tips for Fast Development

1. **Keep the image built**: After first build, use `-NoBuild` for instant starts
2. **Use logs in separate terminal**: Run `.\build-and-run.ps1 -Logs` while testing
3. **Don't rebuild unless needed**: Only rebuild when you change C# code
4. **Use --rm flag**: Container auto-removes when stopped (already in script)
5. **Stop before building**: Always stop container before rebuilding

## Keyboard Shortcuts

When container is running (with `-Run`):
- **Ctrl+C**: Stop container and exit
- Container logs appear in real-time

When viewing logs (with `-Logs`):
- **Ctrl+C**: Stop following logs (container keeps running)

## Environment Variables

The script automatically sets:
- `AzureWebJobsStorage=UseDevelopmentStorage=true` (Azurite)
- `BLOB_CONTAINER_NAME=batch-runs`

To customize, edit the script or run Docker manually:
```powershell
docker run --rm -p 7076:80 `
  -e AzureWebJobsStorage="your-connection-string" `
  -e BLOB_CONTAINER_NAME="your-container" `
  --name videogen `
  videogenerator:local
```

## Performance Notes

| Operation | First Time | With Cache | Reason |
|-----------|-----------|------------|--------|
| Build | 5-10 min | 1-2 min | Downloads .NET SDK, FFMPEG |
| Run (no build) | 5 sec | 5 sec | Just starts container |
| Stop | <1 sec | <1 sec | Instant |
| Video generation | 30-90 sec | 30-90 sec | Depends on audio length |

## Next Steps

After getting the function running:
1. See [TESTING.md](TESTING.md) for comprehensive testing guide
2. See [VSCODE-DEBUG.md](VSCODE-DEBUG.md) for debugging strategies
3. See [README.md](README.md) for full documentation

## Complete Example Session

```powershell
# Terminal 1: Start Azurite
azurite

# Terminal 2: Build and run VideoGenerator (first time)
cd lambdas\VideoGenerator
.\build-and-run.ps1 -Run

# Wait 5-10 minutes for build...
# Container is now running on port 7076

# Terminal 3: Run a test
cd lambdas\VideoGenerator
.\test-function.ps1 -StorageFolder "test-20240101"

# Success! Video generated.

# Back to Terminal 2: Stop with Ctrl+C

# Next day...

# Terminal 1: Azurite still running from yesterday (or restart it)

# Terminal 2: Quick start (5 seconds)
.\build-and-run.ps1 -Run -NoBuild

# Terminal 3: Test again
.\test-function.ps1 -StorageFolder "test-20240101"
```

That's it! The `build-and-run.ps1` script handles all Docker complexity for you.
