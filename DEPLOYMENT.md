# Deployment Guide

This guide explains how to deploy each Azure Function individually, either manually or via GitHub Actions.

## Table of Contents
- [GitHub Actions (Automated)](#github-actions-automated)
- [Manual Deployment (PowerShell)](#manual-deployment-powershell)
- [Local Development](#local-development)
- [Deployment Order](#deployment-order)

---

## GitHub Actions (Automated)

Each function has its own workflow that automatically deploys when changes are pushed to the `master` branch.

### Individual Workflows

| Function | Workflow File | Triggers On |
|----------|--------------|-------------|
| RssProcessor | [deploy-rss-processor.yml](.github/workflows/deploy-rss-processor.yml) | Changes to `lambdas/RssProcessor/**` |
| ArticleSimplifier | [deploy-article-simplifier.yml](.github/workflows/deploy-article-simplifier.yml) | Changes to `lambdas/ArticleSimplifier/**` |
| ImageGenerator | [deploy-image-generator.yml](.github/workflows/deploy-image-generator.yml) | Changes to `lambdas/ImageGenerator/**` |
| NewspaperOrchestrator | [deploy-orchestrator.yml](.github/workflows/deploy-orchestrator.yml) | Changes to `lambdas/NewspaperOrchestrator/**` |

### Manual Trigger via GitHub UI

You can also manually trigger any workflow:

1. Go to **Actions** tab in GitHub
2. Select the workflow you want to run (e.g., "Deploy RssProcessor")
3. Click **Run workflow**
4. Select branch (usually `master`)
5. Click **Run workflow**

### Deploy All Functions at Once

Use the **Deploy All Azure Functions (Manual)** workflow:
- This workflow is **manual-only** (doesn't auto-trigger on push)
- Deploys all four functions in sequence
- Useful for initial setup or when making cross-function changes

---

## Manual Deployment (PowerShell)

Use these scripts to deploy from your local machine:

### Prerequisites
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) installed
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) installed
- Logged into Azure: `az login`

### Deploy Individual Functions

```powershell
# Navigate to scripts directory
cd scripts

# Deploy RssProcessor
.\deploy-rss-processor.ps1

# Deploy ArticleSimplifier
.\deploy-article-simplifier.ps1

# Deploy ImageGenerator
.\deploy-image-generator.ps1

# Deploy NewspaperOrchestrator (must be deployed AFTER the other three)
.\deploy-orchestrator.ps1
```

Each script will:
1. Build and publish the .NET project
2. Create a deployment package (zip)
3. Deploy to Azure Function App
4. Display the function URL

The **Orchestrator script** additionally:
- Retrieves function keys from the other three functions
- Configures environment variables with URLs + keys
- This ensures the 401 Unauthorized error is fixed!

---

## Local Development

### Running Functions Locally

Each function can be run locally using Azure Functions Core Tools:

```powershell
# Install Azure Functions Core Tools (if not already installed)
npm install -g azure-functions-core-tools@4

# Navigate to function directory
cd lambdas/RssProcessor

# Run locally
func start
```

### Local Settings

Each function needs a `local.settings.json` file. Example for RssProcessor:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "OPENAI_API_KEY": "your-openai-key-here"
  }
}
```

For **NewspaperOrchestrator**, you need URLs for the other functions:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",

    "RSS_PROCESSOR_URL": "http://localhost:7071/api/RssProcessor",
    "ARTICLE_SIMPLIFIER_URL": "http://localhost:7072/api/ArticleSimplifier",
    "IMAGE_GENERATOR_URL": "http://localhost:7073/api/ImageGenerator",

    "DEFAULT_RSS_URL": "https://ct24.ceskatelevize.cz/rss/tema/vyber-redakce-84313",
    "BLOB_CONTAINER_NAME": "batch-runs"
  }
}
```

**Note:** Run each function on a different port to avoid conflicts.

---

## Deployment Order

When deploying for the first time or after changes:

### 1. Deploy Supporting Functions First (any order)
- RssProcessor
- ArticleSimplifier
- ImageGenerator

### 2. Deploy Orchestrator Last
- NewspaperOrchestrator
- This retrieves function keys from the other three
- Automatically configures environment variables with authentication

### Why This Order?

The Orchestrator deployment script:
1. Queries Azure to get URLs and keys for the three supporting functions
2. Configures these as environment variables
3. This fixes the **401 Unauthorized** error automatically!

---

## Fixing 401 Unauthorized Errors

If you see 401 errors when the Orchestrator calls other functions:

### Option 1: Redeploy the Orchestrator
```powershell
cd scripts
.\deploy-orchestrator.ps1
```

This will automatically refresh the function keys.

### Option 2: Manually Update Environment Variables

1. Get function keys from Azure Portal:
   - Go to each Function App → Functions → Click function → Function Keys → Copy "default"

2. Update Orchestrator configuration:
   - Go to **NewspaperOrchestrator** Function App
   - Settings → Configuration → Application settings
   - Update these values:

```
RSS_PROCESSOR_URL=https://ai-newspaper-rss-processor.azurewebsites.net/api/RssProcessor?code=<KEY>

ARTICLE_SIMPLIFIER_URL=https://ai-newspaper-article-simplifier.azurewebsites.net/api/ArticleSimplifier?code=<KEY>

IMAGE_GENERATOR_URL=https://ai-newspaper-image-generator.azurewebsites.net/api/ImageGenerator?code=<KEY>
```

3. Save and restart the function app

---

## Troubleshooting

### Build Fails
```powershell
# Clean and rebuild
cd lambdas/<FunctionName>
dotnet clean
dotnet restore
dotnet build --configuration Release
```

### Deployment Hangs
```powershell
# Check if you're logged into Azure
az account show

# If not, login again
az login
```

### Function Keys Not Found
- Ensure the other functions are deployed first
- Check that function names in Azure match the expected names:
  - `ai-newspaper-rss-processor`
  - `ai-newspaper-article-simplifier`
  - `ai-newspaper-image-generator`
- **If functions aren't showing up**: Check if they have required environment variables set
  - ImageGenerator needs `BLOB_CONTAINER_NAME`
  - All functions need `OPENAI_API_KEY`
- Wait a few minutes after deploying before deploying the Orchestrator
- The deployment scripts now include automatic retry logic

### CORS Issues
The deployment scripts automatically configure CORS for Azure Portal.

To add additional origins:
```powershell
az functionapp cors add \
  --name <function-app-name> \
  --resource-group ai-newspaper-rg \
  --allowed-origins "https://yourdomain.com"
```

---

## Environment Variables Reference

### All Functions
- `OPENAI_API_KEY` - Your OpenAI API key

### ImageGenerator
- `BLOB_CONTAINER_NAME` - Azure Storage container name (default: "batch-runs")

### NewspaperOrchestrator
- `RSS_PROCESSOR_URL` - URL with function key
- `ARTICLE_SIMPLIFIER_URL` - URL with function key
- `IMAGE_GENERATOR_URL` - URL with function key
- `BLOB_CONTAINER_NAME` - Azure Storage container name
- `DEFAULT_RSS_URL` - Default RSS feed URL

---

## Quick Reference

### Deploy Everything (First Time)
```powershell
cd scripts
.\deploy-rss-processor.ps1
.\deploy-article-simplifier.ps1
.\deploy-image-generator.ps1
.\deploy-orchestrator.ps1
```

### Deploy Only Changed Function
```powershell
# If you only changed RssProcessor:
.\deploy-rss-processor.ps1

# Then refresh orchestrator config:
.\deploy-orchestrator.ps1
```

### View Logs
```powershell
# Stream logs from Azure
az webapp log tail \
  --name <function-app-name> \
  --resource-group ai-newspaper-rg
```
