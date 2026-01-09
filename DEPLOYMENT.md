# Deployment Guide

This guide walks you through setting up CI/CD with GitHub Actions to automatically deploy Azure Functions.

## Prerequisites

- Azure Account with active subscription
- Azure CLI installed ([Install Guide](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli))
- GitHub repository access
- Claude API Key

## Step 1: Setup Azure Resources

Run the automated setup script:

### Windows (PowerShell - Recommended)
```powershell
cd scripts
.\setup-azure-resources.ps1
```

### Linux/Mac (Bash)
```bash
cd scripts
chmod +x setup-azure-resources.sh
./setup-azure-resources.sh
```

This script will:
1. Create an Azure Resource Group
2. Create a Storage Account
3. Create an App Service Plan (Consumption/Serverless)
4. Create three Function Apps (one for each lambda)
5. Create a Service Principal for GitHub Actions authentication

### Manual Setup (Alternative)

If you prefer manual setup or the script fails, follow these steps:

#### 1. Login to Azure
```bash
az login
```

#### 2. Create Resource Group
```bash
az group create \
  --name ai-newspaper-rg \
  --location westeurope
```

#### 3. Create Storage Account
```bash
az storage account create \
  --name ainewspaperstorage \
  --resource-group ai-newspaper-rg \
  --location westeurope \
  --sku Standard_LRS
```

#### 4. Create App Service Plan
```bash
az functionapp plan create \
  --name ai-newspaper-plan \
  --resource-group ai-newspaper-rg \
  --location westeurope \
  --sku Y1 \
  --is-linux
```

#### 5. Create Function Apps
```bash
# RSS Processor
az functionapp create \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg \
  --plan ai-newspaper-plan \
  --storage-account ainewspaperstorage \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --os-type Linux

# Article Simplifier
az functionapp create \
  --name ai-newspaper-article-simplifier \
  --resource-group ai-newspaper-rg \
  --plan ai-newspaper-plan \
  --storage-account ainewspaperstorage \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --os-type Linux

# Image Generator
az functionapp create \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --plan ai-newspaper-plan \
  --storage-account ainewspaperstorage \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --os-type Linux
```

#### 6. Create Service Principal
```bash
az ad sp create-for-rbac \
  --name "ai-newspaper-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/ai-newspaper-rg \
  --sdk-auth
```

Save the JSON output - you'll need it for GitHub Secrets.

## Step 2: Configure GitHub Secrets

Go to your GitHub repository:
1. Click **Settings** > **Secrets and variables** > **Actions**
2. Click **New repository secret**

Add the following secrets:

### AZURE_CREDENTIALS
The JSON output from the Service Principal creation. Format:
```json
{
  "clientId": "xxx",
  "clientSecret": "xxx",
  "subscriptionId": "xxx",
  "tenantId": "xxx"
}
```

### AZURE_FUNCTIONAPP_RSS_PROCESSOR
Value: `ai-newspaper-rss-processor`

### AZURE_FUNCTIONAPP_ARTICLE_SIMPLIFIER
Value: `ai-newspaper-article-simplifier`

### AZURE_FUNCTIONAPP_IMAGE_GENERATOR
Value: `ai-newspaper-image-generator`

### CLAUDE_API_KEY
Your Claude API key from https://console.anthropic.com/

## Step 3: Trigger Deployment

The GitHub Actions workflow will automatically trigger on:
- Push to `master` branch
- Changes in the `lambdas/` directory
- Manual trigger via GitHub Actions UI

### Manual Deployment
1. Go to your repository on GitHub
2. Click **Actions** tab
3. Select **Deploy Azure Functions** workflow
4. Click **Run workflow**

### Automatic Deployment
Simply push changes:
```bash
git add .
git commit -m "Update functions"
git push origin master
```

## Step 4: Verify Deployment

After deployment completes:

1. Check GitHub Actions for build status
2. Test the Function URLs:

```bash
# Get Function URLs
az functionapp function show \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg \
  --function-name RssProcessor \
  --query invokeUrlTemplate -o tsv
```

3. Test endpoints:
```bash
# RSS Processor
curl -X POST https://ai-newspaper-rss-processor.azurewebsites.net/api/RssProcessor \
  -H "Content-Type: application/json" \
  -d '{"rssUrl": "https://example.com/rss", "audienceAge": 12}'

# Article Simplifier
curl -X POST https://ai-newspaper-article-simplifier.azurewebsites.net/api/ArticleSimplifier \
  -H "Content-Type: application/json" \
  -d '{"articleUrl": "https://example.com/article", "audienceAge": 12}'

# Image Generator
curl -X POST https://ai-newspaper-image-generator.azurewebsites.net/api/ImageGenerator \
  -H "Content-Type: application/json" \
  -d '{"articleTitle": "Title", "simplifiedArticle": "Text...", "audienceAge": 12}'
```

## Monitoring and Logs

### View Logs in Azure Portal
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to your Function App
3. Click **Log stream** or **Monitor** > **Logs**

### View Logs via CLI
```bash
az webapp log tail \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg
```

## Cost Management

The Azure Functions use a **Consumption Plan** (Y1 SKU):
- Pay only for execution time
- Free tier includes:
  - 1 million requests/month
  - 400,000 GB-s execution time/month
- Estimated cost: $0-5/month for light usage

Monitor costs:
```bash
az consumption usage list \
  --start-date 2026-01-01 \
  --end-date 2026-01-31
```

## Troubleshooting

### Deployment Fails
1. Check GitHub Actions logs
2. Verify all secrets are correctly configured
3. Ensure Function App names are unique globally

### Function Returns 500 Error
1. Check Application Insights logs
2. Verify `CLAUDE_API_KEY` is set correctly:
```bash
az functionapp config appsettings list \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg
```

### Update Secrets
```bash
az functionapp config appsettings set \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg \
  --settings "CLAUDE_API_KEY=new-key-here"
```

## Cleanup

To remove all resources:
```bash
az group delete --name ai-newspaper-rg --yes
```

## Next Steps

- Set up custom domains
- Configure Application Insights for monitoring
- Add API Management for rate limiting
- Set up staging slots for testing
