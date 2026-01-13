# VideoGenerator Azure Container App Deployment Guide

## Overview

The VideoGenerator service is deployed as an **Azure Container App** with **scale-to-zero capability** for cost-effective video processing.

### Why Container App instead of Function App?

**Previous Architecture (Function App B1):**
- Cost: ~$18/month ($13 for B1 plan + $5 for ACR)
- Always running (24/7), even when idle
- Manual scaling only (1-3 instances)
- Required workarounds for Docker support

**Current Architecture (Container App):**
- Cost: ~$5-10/month ($5 for ACR + minimal compute usage)
- **Scales to zero** when idle (no wasted resources)
- Automatic horizontal scaling (0-10 instances)
- Native Docker support with HTTPS endpoints
- **70% cost reduction** 💰

## Architecture

- **Service Type**: Azure Container App (HTTP API)
- **Runtime**: Custom Docker container with .NET 8 + FFMPEG
- **Hosting**: Container App Environment (serverless)
- **Container Registry**: Azure Container Registry (Basic tier)
- **Storage**: Azure Blob Storage (public blob access enabled)
- **Scaling**: 0-10 replicas (scale-to-zero enabled)
- **Resources**: 1 CPU, 2GB Memory per instance

## Azure Resources Created

### 1. Container Registry
- **Name**: `ainewspapervideogen`
- **SKU**: Basic
- **Admin Access**: Enabled (for deployment)
- **URL**: `ainewspapervideogen.azurecr.io`

### 2. Container App Environment
- **Name**: `ai-newspaper-containerapp-env`
- **Location**: West Europe
- **Purpose**: Shared environment for container apps

### 3. Container App
- **Name**: `ai-newspaper-video-generator`
- **Container Image**: `ainewspapervideogen.azurecr.io/videogenerator:latest`
- **Port**: 8080 (internal)
- **Ingress**: External HTTPS
- **Min Replicas**: 0 (scales to zero when idle)
- **Max Replicas**: 10
- **CPU**: 1.0 cores
- **Memory**: 2.0 GB
- **Environment Variables**:
  - `AzureWebJobsStorage`: Storage connection string
  - `BLOB_CONTAINER_NAME`: batch-runs

## API Endpoints

### Health Check
```
GET https://ai-newspaper-video-generator.<region>.azurecontainerapps.io/health
```

Response:
```json
{
  "status": "healthy",
  "timestamp": "2024-01-13T10:30:00Z"
}
```

### Generate Videos (Batch)
```
POST https://ai-newspaper-video-generator.<region>.azurecontainerapps.io/api/generate
Content-Type: application/json

{
  "storageFolders": [
    "age-8/2024-01-13/article-0",
    "age-8/2024-01-13/article-1",
    "age-8/2024-01-13/article-2"
  ]
}
```

Response:
```json
{
  "results": [
    {
      "folder": "age-8/2024-01-13/article-0",
      "success": true,
      "videoUrl": "https://ainewspaperstorage.blob.core.windows.net/batch-runs/age-8/2024-01-13/article-0/video.mp4",
      "error": null
    },
    {
      "folder": "age-8/2024-01-13/article-1",
      "success": true,
      "videoUrl": "https://ainewspaperstorage.blob.core.windows.net/batch-runs/age-8/2024-01-13/article-1/video.mp4",
      "error": null
    },
    {
      "folder": "age-8/2024-01-13/article-2",
      "success": false,
      "videoUrl": null,
      "error": "Failed to download audio file"
    }
  ]
}
```

## Deployment Process

### Initial Setup

Use the standalone setup script to create all resources:

```powershell
cd scripts
.\setup-video-generator-aca.ps1
```

The script will:
1. ✅ Install Container App CLI extension (if needed)
2. ✅ Check prerequisites (resource group, storage account)
3. ✅ Create Azure Container Registry
4. ✅ Create Container App Environment
5. ✅ Create Container App with scale-to-zero configuration
6. ✅ Configure environment variables

### GitHub Actions Deployment

The deployment workflow ([`.github/workflows/deploy-video-generator-aca.yml`](../../.github/workflows/deploy-video-generator-aca.yml)) automatically:

1. **Builds** the Docker image using `az acr build` (builds in Azure, no local Docker needed)
2. **Tags** with both `:latest` and `:${github.sha}`
3. **Updates** the Container App with new image
4. **Configures** app settings with storage connection string
5. **Outputs** the Container App URL

**Triggers**: Pushes to `master` that modify `containers/VideoGenerator/**`

### Manual Deployment

To manually build and push the container:

```powershell
# Login to Azure
az login

# Build and push to ACR (builds in Azure)
cd containers/VideoGenerator
az acr build `
  --registry ainewspapervideogen `
  --image videogenerator:latest `
  --file Dockerfile `
  .

# Update Container App (pulls new image automatically)
az containerapp update `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --image ainewspapervideogen.azurecr.io/videogenerator:latest
```

## Local Development

For local testing, use Docker with Azurite:

```powershell
cd containers/VideoGenerator

# Build and run locally
.\build-and-run.ps1 -Build -Run

# Test with single folder
.\test-api.ps1 -StorageFolders "test-20240113/article-0"

# Test with multiple folders (batch)
.\test-api.ps1 -StorageFolders "test-20240113/article-0,test-20240113/article-1"

# Stop container
.\build-and-run.ps1 -Stop
```

### VS Code Integration

Launch configurations are available:
- **"Run VideoGenerator Container App"**: Build and run in Docker locally

Tasks available:
- **"docker build: VideoGenerator Container App"**: Build image only
- **"docker run: VideoGenerator Container App"**: Run existing image
- **"docker build and run: VideoGenerator Container App"**: Build and run

## Cost Estimation

| Resource | SKU | Estimated Monthly Cost |
|----------|-----|----------------------|
| Container Registry | Basic | ~$5 USD |
| Container App Environment | Consumption | $0 USD (included) |
| Container App (idle most of time) | Scale-to-zero | ~$0-5 USD |
| Storage (included in main account) | - | Minimal |
| **Total** | | **~$5-10 USD/month** |

**Previous cost (Function App B1):** ~$18 USD/month
**Savings:** ~$8-13 USD/month (45-70% reduction)

*Cost breakdown:*
- When idle (0 replicas): $0/hour
- When active (1 replica): ~$0.03/hour for compute
- Example: 2 hours/day active = $1.80/month

*Note: Actual costs may vary based on usage and region.*

## Monitoring

### View Logs

```powershell
# Stream logs in real-time
az containerapp logs show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --follow

# View recent logs
az containerapp logs show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --tail 100
```

### Application Insights

Application Insights is automatically integrated:
- **View**: Azure Portal > Container App > Monitoring > Application Insights
- **Query logs**: Use Kusto Query Language (KQL)

### Scaling Metrics

```powershell
# Check current replica count
az containerapp show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --query properties.template.scale.minReplicas,properties.template.scale.maxReplicas
```

## Troubleshooting

### Container Won't Start

```powershell
# Check container status
az containerapp show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --query properties.runningStatus

# Check recent revisions
az containerapp revision list `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --output table

# View deployment logs
az containerapp logs show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --type system
```

### Cold Start Issues

When scaled to zero, first request after idle period may take 10-30 seconds to respond (cold start). This is normal for scale-to-zero architecture.

**Solutions:**
- Keep min replicas at 1 if cold start is unacceptable (increases cost)
- Use async patterns in orchestrator to avoid timeouts
- Pre-warm by calling health endpoint before batch processing

### Storage Access Issues

Verify the storage connection string is set:
```powershell
az containerapp show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --query properties.template.containers[0].env
```

### Container Registry Issues

Verify registry credentials are configured:
```powershell
az containerapp show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --query properties.configuration.registries
```

## Scaling Configuration

Current configuration:
- **Min Replicas**: 0 (scale-to-zero enabled)
- **Max Replicas**: 10
- **Scale Rule**: HTTP requests (default)

To adjust scaling:

```powershell
# Set minimum replicas (disable scale-to-zero)
az containerapp update `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --min-replicas 1

# Adjust maximum replicas
az containerapp update `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --max-replicas 20

# Add custom scale rule (scale based on HTTP queue length)
az containerapp update `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --scale-rule-name http-rule `
  --scale-rule-type http `
  --scale-rule-http-concurrency 10
```

## Updating the Container

The container app automatically pulls the `:latest` tag. To update:

1. **Via GitHub Actions**: Push to master
2. **Manually**:
   ```powershell
   # Build new image
   az acr build --registry ainewspapervideogen --image videogenerator:latest .

   # Container App automatically detects and pulls latest
   # Or force immediate update:
   az containerapp update `
     --name ai-newspaper-video-generator `
     --resource-group ai-newspaper-rg `
     --image ainewspapervideogen.azurecr.io/videogenerator:latest
   ```

## Security

### Container Registry
- Admin access is enabled for deployment
- Credentials are stored as Container App secrets (encrypted at rest)
- Use managed identity for production environments

### Storage Account
- Public blob access enabled for generated videos
- Connection string stored as Container App environment variable (encrypted)
- 30-day lifecycle policy automatically deletes old content

### Container App
- HTTPS only (enforced by default)
- External ingress for API access
- Environment variables encrypted at rest
- No authentication on endpoints (add authentication for production)

## Integration with Orchestrator

The NewspaperOrchestrator calls VideoGenerator after processing all articles:

```csharp
// Step 5: Generate videos for all articles in batch
var videoRequest = new VideoGeneratorRequest
{
    StorageFolders = processedArticles
        .Select((_, i) => $"{request.StorageFolder}/article-{i}")
        .ToArray()
};

var videoResult = await context.CallActivityAsync<VideoGeneratorResponse>(
    nameof(GenerateVideos),
    videoRequest);
```

**Environment Variable Required:**
```
VIDEO_GENERATOR_URL=https://ai-newspaper-video-generator.<region>.azurecontainerapps.io/api/generate
```

## Migration from Function App B1

If migrating from the legacy Function App:

1. Deploy Container App using setup script
2. Update orchestrator environment variable `VIDEO_GENERATOR_URL`
3. Test the new endpoint
4. Delete old Function App and App Service Plan:
   ```powershell
   az functionapp delete --name ai-newspaper-video-generator --resource-group ai-newspaper-rg
   az appservice plan delete --name ai-newspaper-video-generator-plan --resource-group ai-newspaper-rg
   ```

## Next Steps

1. ✅ Build and test locally using [build-and-run.ps1](build-and-run.ps1)
2. ✅ Deploy using `setup-video-generator-aca.ps1`
3. ✅ Push to master to trigger GitHub Actions deployment
4. ✅ Update orchestrator `VIDEO_GENERATOR_URL` environment variable
5. ⚠️ Consider adding API key authentication for production
6. ⚠️ Monitor costs and adjust scaling as needed
7. ⚠️ Set up alerts for failures in Application Insights
