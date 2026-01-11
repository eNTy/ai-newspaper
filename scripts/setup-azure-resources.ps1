# Azure Resources Setup Script for AI Newspaper (PowerShell)
# This script creates all necessary Azure resources for the project

# Configuration
$ResourceGroup = "ai-newspaper-rg"
$Location = "westeurope"
$StorageAccount = "ainewspaperstorage"

# Function App Names
$RssProcessorApp = "ai-newspaper-rss-processor"
$ArticleSimplifierApp = "ai-newspaper-article-simplifier"
$ImageGeneratorApp = "ai-newspaper-image-generator"
$OrchestratorApp = "ai-newspaper-orchestrator"

Write-Host "=================================="
Write-Host "AI Newspaper - Azure Setup"
Write-Host "=================================="
Write-Host ""

# Check if Azure CLI is installed
try {
    $azVersion = az version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI not found"
    }
} catch {
    Write-Host "Error: Azure CLI is not installed. Please install it first." -ForegroundColor Red
    Write-Host "Visit: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    Read-Host "Press Enter to exit"
    exit 1
}

# Check if already logged in
Write-Host "Checking Azure login status..."
$accountInfo = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Not logged in to Azure. Opening browser for login..." -ForegroundColor Yellow
    Write-Host ""
    az login
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Error: Azure login failed." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
} else {
    Write-Host "Already logged in to Azure." -ForegroundColor Green
}

# Select subscription (if multiple)
Write-Host ""
Write-Host "Selecting Azure subscription..."
az account list --output table
Write-Host ""
$subscriptionId = Read-Host "Enter subscription ID (or press Enter for default)"
if ($subscriptionId) {
    az account set --subscription $subscriptionId
}

$selectedSub = az account show --query name -o tsv
Write-Host "Using subscription: $selectedSub" -ForegroundColor Green
Write-Host ""

# Create Resource Group
Write-Host "Creating resource group: $ResourceGroup..." -ForegroundColor Cyan
az group create `
    --name $ResourceGroup `
    --location $Location `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create resource group" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Create Storage Account
Write-Host ""
Write-Host "Creating storage account: $StorageAccount..." -ForegroundColor Cyan
az storage account create `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2 `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create storage account" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Get Storage Connection String
$storageConnection = az storage account show-connection-string `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --query connectionString `
    --output tsv

# Create blob container for images
Write-Host ""
Write-Host "Creating blob container: batch-runs..." -ForegroundColor Cyan
az storage container create `
    --name "batch-runs" `
    --account-name $StorageAccount `
    --connection-string $storageConnection `
    --public-access off `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create blob container" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Set lifecycle management policy to delete blobs after 30 days
Write-Host ""
Write-Host "Setting up lifecycle management policy (delete after 30 days)..." -ForegroundColor Cyan
$lifecyclePolicy = @"
{
  "rules": [
    {
      "enabled": true,
      "name": "delete-old-batch-runs",
      "type": "Lifecycle",
      "definition": {
        "actions": {
          "baseBlob": {
            "delete": {
              "daysAfterModificationGreaterThan": 30
            }
          }
        },
        "filters": {
          "blobTypes": [
            "blockBlob"
          ],
          "prefixMatch": [
            "batch-runs/"
          ]
        }
      }
    }
  ]
}
"@

$policyFile = "$env:TEMP\lifecycle-policy.json"
$lifecyclePolicy | Out-File -FilePath $policyFile -Encoding utf8

az storage account management-policy create `
    --account-name $StorageAccount `
    --resource-group $ResourceGroup `
    --policy "@$policyFile"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create lifecycle management policy" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Remove-Item $policyFile

# Note: For Azure Functions, we'll use Consumption plan (no need to create separate plan)
# Azure will create it automatically with the function apps
Write-Host ""
Write-Host "Note: Using Consumption plan (serverless) - no separate plan creation needed" -ForegroundColor Yellow

# Create Function Apps (Consumption/Serverless plan)
Write-Host ""
Write-Host "Creating Function App: $RssProcessorApp..." -ForegroundColor Cyan
az functionapp create `
    --name $RssProcessorApp `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --storage-account $StorageAccount `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create RSS Processor function app" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Creating Function App: $ArticleSimplifierApp..." -ForegroundColor Cyan
az functionapp create `
    --name $ArticleSimplifierApp `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --storage-account $StorageAccount `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create Article Simplifier function app" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Creating Function App: $ImageGeneratorApp..." -ForegroundColor Cyan
az functionapp create `
    --name $ImageGeneratorApp `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --storage-account $StorageAccount `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create Image Generator function app" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Creating Function App: $OrchestratorApp..." -ForegroundColor Cyan
az functionapp create `
    --name $OrchestratorApp `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --storage-account $StorageAccount `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create Orchestrator function app" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Create Service Principal for GitHub Actions
Write-Host ""
Write-Host "Creating Service Principal for GitHub Actions..." -ForegroundColor Cyan
$subscriptionId = az account show --query id -o tsv

$spOutput = az ad sp create-for-rbac `
    --name "ai-newspaper-github-actions" `
    --role contributor `
    --scopes "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup" `
    --sdk-auth

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create Service Principal" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Storage Account: $StorageAccount"
Write-Host "Function Apps:"
Write-Host "  - $RssProcessorApp"
Write-Host "  - $ArticleSimplifierApp"
Write-Host "  - $ImageGeneratorApp"
Write-Host "  - $OrchestratorApp"
Write-Host ""
Write-Host "==================================" -ForegroundColor Yellow
Write-Host "GitHub Secrets Configuration" -ForegroundColor Yellow
Write-Host "==================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "Add the following secrets to your GitHub repository:" -ForegroundColor Yellow
Write-Host "(Settings > Secrets and variables > Actions > New repository secret)"
Write-Host ""
Write-Host "1. AZURE_CREDENTIALS" -ForegroundColor Cyan
Write-Host "   Value:"
Write-Host $spOutput
Write-Host ""
Write-Host "2. AZURE_FUNCTIONAPP_RSS_PROCESSOR" -ForegroundColor Cyan
Write-Host "   Value: $RssProcessorApp"
Write-Host ""
Write-Host "3. AZURE_FUNCTIONAPP_ARTICLE_SIMPLIFIER" -ForegroundColor Cyan
Write-Host "   Value: $ArticleSimplifierApp"
Write-Host ""
Write-Host "4. AZURE_FUNCTIONAPP_IMAGE_GENERATOR" -ForegroundColor Cyan
Write-Host "   Value: $ImageGeneratorApp"
Write-Host ""
Write-Host "5. AZURE_FUNCTIONAPP_ORCHESTRATOR" -ForegroundColor Cyan
Write-Host "   Value: $OrchestratorApp"
Write-Host ""
Write-Host "6. OPENAI_API_KEY" -ForegroundColor Cyan
Write-Host "   Value: <your-openai-api-key-from-platform.openai.com>"
Write-Host ""
Write-Host "Note: Get your OpenAI API key from https://platform.openai.com/api-keys" -ForegroundColor Yellow
Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Next Steps" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host "1. Get your OpenAI API key from https://platform.openai.com/api-keys"
Write-Host "2. Add the above secrets to GitHub"
Write-Host "3. Push code to trigger deployment"
Write-Host "4. Monitor deployment in GitHub Actions tab"
Write-Host ""
Read-Host "Press Enter to exit"
