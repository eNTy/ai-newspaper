# Azure Container App Setup Script for VideoGenerator (PowerShell)
# This script creates the VideoGenerator as an Azure Container App with scale-to-zero capability

# Configuration
$ResourceGroup = "ai-newspaper-rg"
$Location = "westeurope"
$StorageAccount = "ainewspaperstorage"
$ContainerRegistry = "ainewspapervideogen"
$ContainerAppName = "ai-newspaper-video-generator"
$ContainerAppEnv = "ai-newspaper-containerapp-env"

Write-Host "==================================" -ForegroundColor Green
Write-Host "VideoGenerator - Azure Container App Setup" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
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

# Install Container App extension if not already installed
Write-Host ""
Write-Host "Checking Container App extension..." -ForegroundColor Cyan
$extensionInstalled = az extension list --query "[?name=='containerapp'].name" -o tsv
if ([string]::IsNullOrEmpty($extensionInstalled)) {
    Write-Host "Installing Container App extension..." -ForegroundColor Yellow
    az extension add --name containerapp
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to install Container App extension" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    Write-Host "Container App extension installed." -ForegroundColor Green
} else {
    Write-Host "Container App extension already installed." -ForegroundColor Green
}

# Create Azure Container Registry if it doesn't exist
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

# Create Container App Environment if it doesn't exist
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

# Get Container Registry credentials
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

# Get Storage Connection String
Write-Host ""
Write-Host "Retrieving Storage connection string..." -ForegroundColor Cyan
$storageConnection = az storage account show-connection-string `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --query connectionString `
    --output tsv

if ([string]::IsNullOrEmpty($storageConnection)) {
    Write-Host "Error: Failed to retrieve Storage connection string" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Storage connection string retrieved successfully." -ForegroundColor Green

# Check if Container App exists
Write-Host ""
Write-Host "Checking if Container App exists: $ContainerAppName..." -ForegroundColor Cyan
$appExists = az containerapp show --name $ContainerAppName --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "Container App already exists. Updating..." -ForegroundColor Yellow

    # Update existing container app
    az containerapp update `
        --name $ContainerAppName `
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
    # Create new Container App
    Write-Host "Creating Container App: $ContainerAppName..." -ForegroundColor Cyan
    az containerapp create `
        --name $ContainerAppName `
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

# Get Container App URL
Write-Host ""
Write-Host "Retrieving Container App URL..." -ForegroundColor Cyan
$appUrl = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query properties.configuration.ingress.fqdn `
    --output tsv

if ([string]::IsNullOrEmpty($appUrl)) {
    Write-Host "Warning: Could not retrieve Container App URL" -ForegroundColor Yellow
} else {
    $fullUrl = "https://$appUrl"
    Write-Host "Container App URL: $fullUrl" -ForegroundColor Green
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Resources created/configured:" -ForegroundColor Cyan
Write-Host "  - Container Registry: $ContainerRegistry" -ForegroundColor Cyan
Write-Host "  - Container App Environment: $ContainerAppEnv" -ForegroundColor Cyan
Write-Host "  - Container App: $ContainerAppName" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Registry URL: https://$acrLoginServer" -ForegroundColor Yellow
if (-not [string]::IsNullOrEmpty($appUrl)) {
    Write-Host "Container App URL: $fullUrl" -ForegroundColor Yellow
    Write-Host "  - Health: $fullUrl/health" -ForegroundColor Yellow
    Write-Host "  - Generate: POST $fullUrl/api/generate" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Cost Estimate:" -ForegroundColor Yellow
Write-Host "  - Container Registry (Basic): ~$5/month" -ForegroundColor Cyan
Write-Host "  - Container App Environment: ~$0/month (included)" -ForegroundColor Cyan
Write-Host "  - Container App (scale-to-zero): ~$0-5/month (pay-per-use)" -ForegroundColor Cyan
Write-Host "  - Total: ~$5-10/month vs $18/month with B1 Function App" -ForegroundColor Green
Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Next Steps" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host "1. Build and push your Docker image:" -ForegroundColor Yellow
Write-Host "   cd containers/VideoGenerator"
Write-Host "   az acr build --registry $ContainerRegistry --image videogenerator:latest --file Dockerfile ."
Write-Host ""
Write-Host "2. Or use GitHub Actions to deploy automatically on push" -ForegroundColor Yellow
Write-Host ""
Write-Host "3. Test the endpoint:" -ForegroundColor Yellow
if (-not [string]::IsNullOrEmpty($appUrl)) {
    Write-Host "   Invoke-RestMethod -Uri '$fullUrl/health' -Method Get"
}
Write-Host ""
Write-Host "4. View logs:" -ForegroundColor Yellow
Write-Host "   az containerapp logs show --name $ContainerAppName --resource-group $ResourceGroup --follow"
Write-Host ""
Write-Host "5. Update orchestrator to use new endpoint" -ForegroundColor Yellow
Write-Host ""
Read-Host "Press Enter to exit"
