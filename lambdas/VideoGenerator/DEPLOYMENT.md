# VideoGenerator Azure Deployment Guide

## Overview

The VideoGenerator function is deployed as a **Docker containerized Azure Function** on a **B1 App Service Plan**.

## Architecture

- **Function Type**: HTTP Triggered Azure Function (Docker)
- **Hosting**: App Service Plan (B1 SKU - Basic tier)
- **Container Registry**: Azure Container Registry (Basic tier)
- **Runtime**: Custom Docker container with .NET 8 isolated + FFMPEG
- **Storage**: Azure Blob Storage (public blob access enabled)

## Why App Service Plan instead of Consumption Plan?

Custom Docker containers are **not supported** on Azure Functions Consumption Plan. The B1 (Basic) tier is the most cost-effective option that supports:
- Custom Docker images
- Always-on functionality
- Better performance for video processing workloads

## Azure Resources Created

### 1. Container Registry
- **Name**: `ainewspapervideogen`
- **SKU**: Basic
- **Admin Access**: Enabled (for deployment)
- **URL**: `ainewspapervideogen.azurecr.io`

### 2. App Service Plan
- **Name**: `ai-newspaper-video-generator-plan`
- **SKU**: B1 (Basic)
- **OS**: Linux
- **Location**: West Europe

### 3. Function App
- **Name**: `ai-newspaper-video-generator`
- **Functions Version**: 4
- **Container Image**: `ainewspapervideogen.azurecr.io/videogenerator:latest`
- **App Settings**:
  - `DOCKER_REGISTRY_SERVER_URL`: Container registry URL
  - `DOCKER_REGISTRY_SERVER_USERNAME`: ACR admin username
  - `DOCKER_REGISTRY_SERVER_PASSWORD`: ACR admin password
  - `WEBSITES_ENABLE_APP_SERVICE_STORAGE`: false
  - `BLOB_CONTAINER_NAME`: batch-runs
  - `AzureWebJobsStorage`: Storage connection string

## Deployment Process

### Setup Script
Use the standalone setup script to create all resources:

```powershell
cd scripts
.\setup-video-generator.ps1
```

The script will:
1. ✅ Check prerequisites (resource group, storage account)
2. ✅ Create Azure Container Registry
3. ✅ Create App Service Plan (B1)
4. ✅ Create Function App
5. ✅ Configure container registry credentials
6. ✅ Set container image

### GitHub Actions Deployment
The deployment workflow ([`.github/workflows/deploy-video-generator.yml`](../../.github/workflows/deploy-video-generator.yml)) automatically:

1. **Builds** the Docker image using `az acr build` (builds in Azure, no local Docker needed)
2. **Tags** with both `:latest` and `:${github.sha}`
3. **Configures** app settings with storage connection string
4. **Restarts** the function app to apply changes

**Triggers**: Pushes to `master` that modify `lambdas/VideoGenerator/**`

### Manual Deployment
To manually build and push the container:

```powershell
# Login to Azure
az login

# Build and push to ACR (builds in Azure)
cd lambdas/VideoGenerator
az acr build `
  --registry ainewspapervideogen `
  --image videogenerator:latest `
  --file Dockerfile `
  .

# The function app will automatically pull the new image
az functionapp restart `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg
```

## Local Development

For local testing, use Docker with Azurite:

```powershell
cd lambdas/VideoGenerator

# Build and run locally
.\build-and-run.ps1 -Run

# Test
.\test-function.ps1 -StorageFolder "test-20240101"
```

See [QUICK-START.md](QUICK-START.md) for detailed local development guide.

## Function URL

- **Production**: `https://ai-newspaper-video-generator.azurewebsites.net/api/VideoGenerator`
- **Local**: `http://localhost:7076/api/VideoGenerator`

## Cost Estimation

| Resource | SKU | Estimated Monthly Cost |
|----------|-----|----------------------|
| App Service Plan | B1 | ~$13 USD |
| Container Registry | Basic | ~$5 USD |
| Storage (included in main account) | - | Minimal |
| **Total** | | **~$18 USD/month** |

*Note: Actual costs may vary based on usage and region.*

## Monitoring

### View Logs
```powershell
# Stream logs
az functionapp log tail `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg

# Or via Azure Portal
# Navigate to: Function App > Log stream
```

### Application Insights
Application Insights is automatically created for the function app:
- **Name**: `ai-newspaper-video-generator`
- **View**: Azure Portal > Application Insights > Logs

## Troubleshooting

### Container Won't Start
```powershell
# Check container logs
az functionapp log deployment show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg

# Verify container image
az functionapp config container show `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg
```

### Authentication Issues
The function uses `AuthorizationLevel.Anonymous` for the HTTP trigger. No authentication key is required.

### Storage Access Issues
Verify the storage connection string is set:
```powershell
az functionapp config appsettings list `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --query "[?name=='AzureWebJobsStorage']"
```

### Container Registry Issues
Verify registry credentials are configured:
```powershell
az functionapp config appsettings list `
  --name ai-newspaper-video-generator `
  --resource-group ai-newspaper-rg `
  --query "[?starts_with(name, 'DOCKER_')]"
```

## Scaling

The B1 plan supports:
- **Manual scaling**: 1-3 instances
- **Auto-scaling**: Not available on B1 (requires S1 or higher)

To scale manually:
```powershell
az appservice plan update `
  --name ai-newspaper-video-generator-plan `
  --resource-group ai-newspaper-rg `
  --number-of-workers 2
```

## Updating the Container

The function app automatically pulls the `:latest` tag. To update:

1. **Via GitHub Actions**: Push to master
2. **Manually**:
   ```powershell
   # Build new image
   az acr build --registry ainewspapervideogen --image videogenerator:latest .

   # Restart function to pull latest
   az functionapp restart --name ai-newspaper-video-generator --resource-group ai-newspaper-rg
   ```

## Security

### Container Registry
- Admin access is enabled for deployment
- Credentials are stored as Function App settings (encrypted at rest)
- Use RBAC for production environments

### Storage Account
- Public blob access enabled for generated videos
- Connection string stored as Function App setting (encrypted)
- 30-day lifecycle policy automatically deletes old content

### Function App
- HTTPS only (enforced by default)
- Anonymous HTTP trigger for testing
- Consider adding Function-level authorization for production

## Next Steps

1. ✅ Build and test locally using [QUICK-START.md](QUICK-START.md)
2. ✅ Deploy using `setup-video-generator.ps1`
3. ✅ Push to master to trigger GitHub Actions deployment
4. ⚠️ Consider adding API key authentication for production
5. ⚠️ Monitor costs and scale as needed
