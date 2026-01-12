# Azure VideoGenerator Setup Script (PowerShell)
# This script creates the VideoGenerator function app and container registry

# Configuration
$ResourceGroup = "ai-newspaper-rg"
$Location = "westeurope"
$StorageAccount = "ainewspaperstorage"
$VideoGeneratorApp = "ai-newspaper-video-generator"
$ContainerRegistry = "ainewspapervideogen"

Write-Host "=================================="
Write-Host "VideoGenerator - Azure Setup"
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

$selectedSub = az account show --query name -o tsv
Write-Host "Using subscription: $selectedSub" -ForegroundColor Green
Write-Host ""

# Check if resource group exists
Write-Host "Checking for resource group: $ResourceGroup..." -ForegroundColor Cyan
$rgExists = az group exists --name $ResourceGroup
if ($rgExists -eq "false") {
    Write-Host "Error: Resource group '$ResourceGroup' does not exist." -ForegroundColor Red
    Write-Host "Please run the main setup-azure-resources.ps1 script first." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Resource group exists." -ForegroundColor Green

# Check if storage account exists
Write-Host ""
Write-Host "Checking for storage account: $StorageAccount..." -ForegroundColor Cyan
$storageExists = az storage account check-name --name $StorageAccount --query nameAvailable -o tsv
if ($storageExists -eq "true") {
    Write-Host "Error: Storage account '$StorageAccount' does not exist." -ForegroundColor Red
    Write-Host "Please run the main setup-azure-resources.ps1 script first." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Storage account exists." -ForegroundColor Green

# Create Azure Container Registry for VideoGenerator
Write-Host ""
Write-Host "Creating Azure Container Registry: $ContainerRegistry..." -ForegroundColor Cyan
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

# Create App Service Plan for VideoGenerator (required for custom containers)
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

# Check if Function App exists and delete if it was created with wrong plan
Write-Host ""
Write-Host "Checking if Function App exists: $VideoGeneratorApp..." -ForegroundColor Cyan
$appExists = az functionapp show --name $VideoGeneratorApp --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -eq 0) {
    # Function app exists - check if it's on the correct plan
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

# Create VideoGenerator Function App if it doesn't exist
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

# Get Container Registry credentials
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

# Configure VideoGenerator app settings with container registry credentials
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

# Configure container image
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

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Resources created/configured:"
Write-Host "  - Container Registry: $ContainerRegistry" -ForegroundColor Cyan
Write-Host "  - Function App: $VideoGeneratorApp" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Registry URL: https://$ContainerRegistry.azurecr.io" -ForegroundColor Yellow
Write-Host "Function App URL: https://$VideoGeneratorApp.azurewebsites.net" -ForegroundColor Yellow
Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Next Steps" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host "1. Build and push your Docker image:" -ForegroundColor Yellow
Write-Host "   cd lambdas/VideoGenerator"
Write-Host "   az acr build --registry $ContainerRegistry --image videogenerator:latest --file Dockerfile ."
Write-Host ""
Write-Host "2. Or use GitHub Actions to deploy automatically on push" -ForegroundColor Yellow
Write-Host ""
Write-Host "3. Monitor deployment:" -ForegroundColor Yellow
Write-Host "   az functionapp show --name $VideoGeneratorApp --resource-group $ResourceGroup"
Write-Host ""
Write-Host "4. View logs:" -ForegroundColor Yellow
Write-Host "   az functionapp log tail --name $VideoGeneratorApp --resource-group $ResourceGroup"
Write-Host ""
Read-Host "Press Enter to exit"
