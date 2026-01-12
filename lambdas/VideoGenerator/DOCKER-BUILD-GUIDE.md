# Docker Build Guide for VideoGenerator

This guide will help you build and test the VideoGenerator Docker container on your local machine.

## Prerequisites

1. **Docker Desktop** installed and running
   - Windows: https://www.docker.com/products/docker-desktop
   - Ensure WSL2 backend is enabled (Settings → General → Use WSL 2 based engine)

2. **Azurite** (Azure Storage Emulator) running
   - Install: `npm install -g azurite`
   - Start: `azurite --silent --location c:\azurite --debug c:\azurite\debug.log`
   - Or use Azure Storage Explorer's built-in emulator

## Quick Start

### Automated Build Test

```powershell
cd lambdas\VideoGenerator
.\test-docker-build.ps1
```

This script will:
- Check if Docker is available and running
- Build the Docker image
- Verify FFMPEG is installed correctly
- Show image size and details
- Display the command to run the container

### Manual Build Steps

If you prefer to run commands manually:

#### 1. Build the Image

```powershell
cd lambdas\VideoGenerator
docker build -t videogenerator:local .
```

**Expected output:**
- Multiple stages: base, build, publish, final
- FFMPEG installation logs
- FFMPEG version verification
- .NET restore and build logs
- Final image created

**Build time:** 5-10 minutes on first build, 1-2 minutes on subsequent builds (with layer caching)

#### 2. Verify the Image

```powershell
# Check image exists
docker images videogenerator:local

# Verify FFMPEG is installed
docker run --rm videogenerator:local ffmpeg -version
docker run --rm videogenerator:local ffprobe -version
```

**Expected FFMPEG output:**
```
ffmpeg version 4.3.x (or higher)
built with gcc ...
configuration: ...
```

#### 3. Run the Container

```powershell
docker run -p 7076:80 `
  -e AzureWebJobsStorage="UseDevelopmentStorage=true" `
  -e BLOB_CONTAINER_NAME="batch-runs" `
  --name videogen-test `
  videogenerator:local
```

**Container startup logs should show:**
```
Azure Functions .NET Worker
info: Host.Startup[...]
info: Microsoft.Azure.Functions.Worker.Extensions[...]
Worker process started and initialized.
```

#### 4. Test the Function

In a new terminal:

```powershell
# Test health endpoint (if available)
curl http://localhost:7076/api/VideoGenerator

# Or use the test script
cd lambdas\VideoGenerator
.\test-function.ps1 -StorageFolder "test-20240101"
```

#### 5. Stop and Clean Up

```powershell
# Stop the container
docker stop videogen-test

# Remove the container
docker rm videogen-test

# Optional: Remove the image
docker rmi videogenerator:local
```

## Docker Build Stages Explained

### Stage 1: Base (Runtime Environment)
```dockerfile
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0 AS base
```
- Starts with official Azure Functions .NET 8 isolated runtime
- Installs FFMPEG and dependencies
- Sets up working directory

### Stage 2: Build
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
```
- Uses .NET 8 SDK for compilation
- Restores NuGet packages
- Builds the project in Release configuration

### Stage 3: Publish
```dockerfile
FROM build AS publish
```
- Publishes the application
- Creates optimized binaries

### Stage 4: Final
```dockerfile
FROM base AS final
```
- Copies published artifacts from publish stage
- Results in minimal final image with only runtime + app + FFMPEG

## Troubleshooting

### Build Fails at FFMPEG Installation

**Error:** `E: Unable to locate package ffmpeg`

**Solution:**
- Check internet connection
- Try rebuilding without cache: `docker build --no-cache -t videogenerator:local .`

### Build Fails at .NET Restore

**Error:** `error NU1301: Unable to load the service index`

**Solution:**
- Check internet connection
- Ensure NuGet sources are accessible
- Try: `docker build --network=host -t videogenerator:local .`

### Container Won't Start

**Error:** `Cannot connect to the Docker daemon`

**Solution:**
- Ensure Docker Desktop is running
- Check Docker Desktop → Settings → Resources
- Restart Docker Desktop

### Function Not Accessible

**Error:** `Connection refused` when accessing http://localhost:7076

**Solution:**
- Check container logs: `docker logs videogen-test`
- Ensure port 7076 is not in use: `netstat -ano | findstr :7076`
- Verify Azure Functions host started successfully in logs

### FFMPEG Not Found in Container

**Error:** `ffmpeg: command not found` in container

**Solution:**
- Rebuild image: `docker build --no-cache -t videogenerator:local .`
- Verify with: `docker run --rm videogenerator:local which ffmpeg`
- Expected output: `/usr/bin/ffmpeg`

### Container Runs But Function Fails

**Error:** Function returns 500 or errors in logs

**Solution:**
- Check environment variables are set correctly
- Ensure Azurite is running and accessible
- Check container logs for specific error messages
- Verify storage connection string

## Performance Optimization

### Reduce Build Time

1. **Use BuildKit** (faster builds):
   ```powershell
   $env:DOCKER_BUILDKIT=1
   docker build -t videogenerator:local .
   ```

2. **Layer Caching**: Keep COPY commands at the end of stages to maximize cache hits

3. **Multi-core Builds**: Docker BuildKit uses multiple cores by default

### Reduce Image Size

Current image size: ~1.5-2 GB (Azure Functions runtime + .NET + FFMPEG + app)

Further optimization options:
1. Use Alpine-based images (more complex, may have compatibility issues)
2. Remove unnecessary packages from FFMPEG installation
3. Multi-stage builds already minimize final image size

## Deployment

### Push to Azure Container Registry

```powershell
# Login to ACR
az acr login --name yourregistry

# Tag the image
docker tag videogenerator:local yourregistry.azurecr.io/videogenerator:v1

# Push to ACR
docker push yourregistry.azurecr.io/videogenerator:v1
```

### Deploy to Azure Functions

```powershell
# Create Function App with container
az functionapp create `
  --name videogenerator-func `
  --storage-account yourstorage `
  --resource-group yourgroup `
  --plan yourplan `
  --deployment-container-image-name yourregistry.azurecr.io/videogenerator:v1 `
  --functions-version 4

# Configure app settings
az functionapp config appsettings set `
  --name videogenerator-func `
  --resource-group yourgroup `
  --settings `
    AzureWebJobsStorage="<connection-string>" `
    BLOB_CONTAINER_NAME="batch-runs"
```

## Verification Checklist

- [ ] Docker Desktop is installed and running
- [ ] Docker build completes without errors
- [ ] Image size is reasonable (1.5-2 GB)
- [ ] FFMPEG version command works in container
- [ ] Container starts without errors
- [ ] Function host initializes successfully
- [ ] Can call function endpoint (even if it fails due to missing data)
- [ ] Azurite is running for local testing
- [ ] Test data is uploaded to storage
- [ ] Function successfully processes test data
- [ ] Generated video.mp4 appears in storage
- [ ] Video is playable and has correct format (1080x1350)
- [ ] Video has zoom/pan effect
- [ ] Video has synced audio
- [ ] Video has title overlay (if article.json provided)

## Next Steps

Once Docker build and local testing are successful:
1. Test with real article data from orchestrator
2. Deploy to Azure Container Registry
3. Create Azure Function App with container
4. Configure environment variables in Azure
5. Integrate with NewspaperOrchestrator
6. Test end-to-end workflow

## Additional Resources

- [Azure Functions Docker Containers](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-function-linux-custom-image)
- [Docker Build Best Practices](https://docs.docker.com/develop/develop-images/dockerfile_best-practices/)
- [FFMPEG Documentation](https://ffmpeg.org/documentation.html)
- [Azure Container Registry](https://docs.microsoft.com/en-us/azure/container-registry/)
