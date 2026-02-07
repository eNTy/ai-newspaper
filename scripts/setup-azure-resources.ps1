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
$TextToSpeechApp = "ai-newspaper-text-to-speech"
$OrchestratorApp = "ai-newspaper-orchestrator"
$InstagramPublisherApp = "ai-newspaper-instagram-publisher"

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
Write-Host "Creating Function App: $TextToSpeechApp..." -ForegroundColor Cyan
az functionapp create `
    --name $TextToSpeechApp `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --storage-account $StorageAccount `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create Text-to-Speech function app" -ForegroundColor Red
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

Write-Host ""
Write-Host "Creating Function App: $InstagramPublisherApp..." -ForegroundColor Cyan
az functionapp create `
    --name $InstagramPublisherApp `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --storage-account $StorageAccount `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create Instagram Publisher function app" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# VideoGenerator uses Azure Container App for scale-to-zero capability (cost-effective)
Write-Host ""
Write-Host "Installing Container App extension if needed..." -ForegroundColor Cyan
$extensionInstalled = az extension list --query "[?name=='containerapp'].name" -o tsv
if ([string]::IsNullOrEmpty($extensionInstalled)) {
    Write-Host "Installing Container App extension..." -ForegroundColor Yellow
    az extension add --name containerapp --upgrade
}

Write-Host ""
Write-Host "Creating Azure Container Registry for VideoGenerator..." -ForegroundColor Cyan
$ContainerRegistry = "ainewspapervideogen"
$VideoGeneratorApp = "ai-newspaper-video-generator"
$ContainerAppEnv = "ai-newspaper-containerapp-env"

$acrExists = az acr check-name --name $ContainerRegistry --query nameAvailable -o tsv
if ($acrExists -eq "true") {
    az acr create `
        --name $ContainerRegistry `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku Basic `
        --admin-enabled true `
        --output table
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to create Container Registry" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "Container Registry created successfully." -ForegroundColor Green
} else {
    Write-Host "Container Registry already exists. Skipping creation." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Creating Container App Environment: $ContainerAppEnv..." -ForegroundColor Cyan
$envExists = az containerapp env show --name $ContainerAppEnv --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    az containerapp env create `
        --name $ContainerAppEnv `
        --resource-group $ResourceGroup `
        --location $Location `
        --output table
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to create Container App Environment" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "Container App Environment created successfully." -ForegroundColor Green
} else {
    Write-Host "Container App Environment already exists." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Retrieving Container Registry credentials..." -ForegroundColor Cyan
$acrUsername = az acr credential show --name $ContainerRegistry --query username -o tsv
$acrPassword = az acr credential show --name $ContainerRegistry --query "passwords[0].value" -o tsv
$acrLoginServer = az acr show --name $ContainerRegistry --query loginServer -o tsv

if ([string]::IsNullOrEmpty($acrUsername) -or [string]::IsNullOrEmpty($acrPassword)) {
    Write-Host "Error: Failed to retrieve Container Registry credentials" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Credentials retrieved successfully." -ForegroundColor Green

Write-Host ""
Write-Host "Checking if Container App exists: $VideoGeneratorApp..." -ForegroundColor Cyan
$appExists = az containerapp show --name $VideoGeneratorApp --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "Container App already exists. Updating..." -ForegroundColor Yellow
    az containerapp update `
        --name $VideoGeneratorApp `
        --resource-group $ResourceGroup `
        --image "$acrLoginServer/videogenerator:latest" `
        --set-env-vars `
            "AzureWebJobsStorage=$storageConnection" `
            "BLOB_CONTAINER_NAME=batch-runs" `
        --output table
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to update Container App" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "Container App updated successfully." -ForegroundColor Green
} else {
    Write-Host "Creating Container App: $VideoGeneratorApp..." -ForegroundColor Cyan
    az containerapp create `
        --name $VideoGeneratorApp `
        --resource-group $ResourceGroup `
        --environment $ContainerAppEnv `
        --image "$acrLoginServer/videogenerator:latest" `
        --registry-server $acrLoginServer `
        --registry-username $acrUsername `
        --registry-password $acrPassword `
        --target-port 8080 `
        --ingress external `
        --min-replicas 0 `
        --max-replicas 10 `
        --env-vars `
            "AzureWebJobsStorage=$storageConnection" `
            "BLOB_CONTAINER_NAME=batch-runs" `
        --cpu 1.0 `
        --memory 2.0Gi `
        --output table
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to create Container App" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "Container App created successfully." -ForegroundColor Green
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
Write-Host "Container Registry: $ContainerRegistry"
Write-Host "Function Apps:"
Write-Host "  - $RssProcessorApp (Consumption)"
Write-Host "  - $ArticleSimplifierApp (Consumption)"
Write-Host "  - $ImageGeneratorApp (Consumption)"
Write-Host "  - $TextToSpeechApp (Consumption)"
Write-Host "  - $OrchestratorApp (Consumption)"
Write-Host "  - $InstagramPublisherApp (Consumption)"
Write-Host "Container Apps:"
Write-Host "  - $VideoGeneratorApp (Scale-to-Zero)"
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
Write-Host "5. AZURE_FUNCTIONAPP_TEXT_TO_SPEECH" -ForegroundColor Cyan
Write-Host "   Value: $TextToSpeechApp"
Write-Host ""
Write-Host "6. AZURE_FUNCTIONAPP_ORCHESTRATOR" -ForegroundColor Cyan
Write-Host "   Value: $OrchestratorApp"
Write-Host ""
Write-Host "7. AZURE_FUNCTIONAPP_INSTAGRAM_PUBLISHER" -ForegroundColor Cyan
Write-Host "   Value: $InstagramPublisherApp"
Write-Host ""
Write-Host "8. OPENAI_API_KEY" -ForegroundColor Cyan
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
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "VideoGenerator Container App" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "The VideoGenerator uses Azure Container App with scale-to-zero."
Write-Host ""
Write-Host "Cost estimate: ~$5-10/month (vs $18/month with Function App B1)" -ForegroundColor Green
Write-Host ""
Write-Host "To deploy the container:"
Write-Host "1. Push to master branch (triggers GitHub Actions)"
Write-Host "   Or manually:"
Write-Host "   cd containers/VideoGenerator"
Write-Host "   az acr build --registry $ContainerRegistry --image videogenerator:latest --file Dockerfile ."
Write-Host ""
Write-Host "2. Get Container App URL:"
Write-Host "   az containerapp show --name $VideoGeneratorApp --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn -o tsv"
Write-Host ""
Read-Host "Press Enter to exit"
