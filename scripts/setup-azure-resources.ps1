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

# VideoGenerator requires custom Docker container with FFMPEG
# Custom containers are NOT supported on Consumption plan - need App Service Plan (B1)
Write-Host ""
Write-Host "Creating Azure Container Registry for VideoGenerator..." -ForegroundColor Cyan
$ContainerRegistry = "ainewspapervideogen"
$VideoGeneratorApp = "ai-newspaper-video-generator"

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
Write-Host "Creating App Service Plan for VideoGenerator..." -ForegroundColor Cyan
$planName = "$VideoGeneratorApp-plan"
$planExists = az appservice plan show --name $planName --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    az appservice plan create `
        --name $planName `
        --resource-group $ResourceGroup `
        --location $Location `
        --is-linux `
        --sku B1 `
        --output table
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to create App Service Plan" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "App Service Plan created successfully." -ForegroundColor Green
} else {
    Write-Host "App Service Plan already exists." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Checking if VideoGenerator Function App exists..." -ForegroundColor Cyan
$appExists = az functionapp show --name $VideoGeneratorApp --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -eq 0) {
    $currentPlan = az functionapp show --name $VideoGeneratorApp --resource-group $ResourceGroup --query "appServicePlanId" -o tsv
    if ($currentPlan -notlike "*$planName*") {
        Write-Host "Function App exists on wrong plan. Deleting..." -ForegroundColor Yellow
        az functionapp delete --name $VideoGeneratorApp --resource-group $ResourceGroup
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Error: Failed to delete existing function app" -ForegroundColor Red
            Read-Host "Press Enter to exit"
            exit 1
        }
        Write-Host "Existing function app deleted." -ForegroundColor Green
        $appExists = $null
    } else {
        Write-Host "Function App already exists on correct plan. Skipping creation..." -ForegroundColor Yellow
    }
}

if ($LASTEXITCODE -ne 0 -or $null -eq $appExists) {
    Write-Host ""
    Write-Host "Creating Function App: $VideoGeneratorApp..." -ForegroundColor Cyan
    az functionapp create `
        --name $VideoGeneratorApp `
        --resource-group $ResourceGroup `
        --plan $planName `
        --storage-account $StorageAccount `
        --functions-version 4 `
        --deployment-container-image-name "mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0" `
        --output table
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to create Video Generator function app" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "Function App created successfully." -ForegroundColor Green
}

Write-Host ""
Write-Host "Retrieving Container Registry credentials..." -ForegroundColor Cyan
$acrUsername = az acr credential show --name $ContainerRegistry --query username -o tsv
$acrPassword = az acr credential show --name $ContainerRegistry --query "passwords[0].value" -o tsv

if ([string]::IsNullOrEmpty($acrUsername) -or [string]::IsNullOrEmpty($acrPassword)) {
    Write-Host "Error: Failed to retrieve Container Registry credentials" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Credentials retrieved successfully." -ForegroundColor Green

Write-Host ""
Write-Host "Configuring container registry credentials..." -ForegroundColor Cyan
az functionapp config appsettings set `
    --name $VideoGeneratorApp `
    --resource-group $ResourceGroup `
    --settings "DOCKER_REGISTRY_SERVER_URL=https://$ContainerRegistry.azurecr.io" `
               "DOCKER_REGISTRY_SERVER_USERNAME=$acrUsername" `
               "DOCKER_REGISTRY_SERVER_PASSWORD=$acrPassword" `
               "WEBSITES_ENABLE_APP_SERVICE_STORAGE=false" `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to configure container registry credentials" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Container registry credentials configured successfully." -ForegroundColor Green

Write-Host ""
Write-Host "Setting container image..." -ForegroundColor Cyan
az functionapp config container set `
    --name $VideoGeneratorApp `
    --resource-group $ResourceGroup `
    --image "$ContainerRegistry.azurecr.io/videogenerator:latest" `
    --registry-server "https://$ContainerRegistry.azurecr.io" `
    --output table
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to set container image" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Container image configured successfully." -ForegroundColor Green

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
Write-Host "  - $VideoGeneratorApp (B1 App Service Plan - Docker)"
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
Write-Host "7. OPENAI_API_KEY" -ForegroundColor Cyan
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
Write-Host "VideoGenerator Deployment" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "The VideoGenerator function uses a custom Docker container."
Write-Host ""
Write-Host "To deploy the container:"
Write-Host "1. Push to master branch (triggers GitHub Actions)"
Write-Host "   Or manually:"
Write-Host "   cd lambdas/VideoGenerator"
Write-Host "   az acr build --registry $ContainerRegistry --image videogenerator:latest --file Dockerfile ."
Write-Host ""
Write-Host "2. Restart function app:"
Write-Host "   az functionapp restart --name $VideoGeneratorApp --resource-group $ResourceGroup"
Write-Host ""
Write-Host "Function URL: https://$VideoGeneratorApp.azurewebsites.net/api/VideoGenerator"
Write-Host ""
Read-Host "Press Enter to exit"
