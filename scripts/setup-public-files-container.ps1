# Setup Public Files Container Script for AI Newspaper (PowerShell)
# This script creates and configures the public-files blob container

# Configuration
$ResourceGroup = "ai-newspaper-rg"
$StorageAccount = "ainewspaperstorage"
$ContainerName = "public-files"

Write-Host "=================================="
Write-Host "AI Newspaper - Public Files Setup"
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

Write-Host ""

# Check if storage account exists
Write-Host "Checking storage account: $StorageAccount..." -ForegroundColor Cyan
$storageExists = az storage account show --name $StorageAccount --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Storage account '$StorageAccount' not found in resource group '$ResourceGroup'" -ForegroundColor Red
    Write-Host "Please run setup-azure-resources.ps1 first to create the storage account." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Storage account found." -ForegroundColor Green

# Get Storage Connection String
Write-Host ""
Write-Host "Retrieving storage connection string..." -ForegroundColor Cyan
$storageConnection = az storage account show-connection-string `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --query connectionString `
    --output tsv

if ([string]::IsNullOrEmpty($storageConnection)) {
    Write-Host "Error: Failed to retrieve storage connection string" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Create blob container with public access
Write-Host ""
Write-Host "Creating blob container: $ContainerName with public blob access..." -ForegroundColor Cyan
az storage container create `
    --name $ContainerName `
    --account-name $StorageAccount `
    --connection-string $storageConnection `
    --public-access blob `
    --output table

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to create blob container" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "Container created successfully with public blob access." -ForegroundColor Green

# Enable static website hosting (optional but useful for public files)
Write-Host ""
Write-Host "Enabling CORS for blob storage..." -ForegroundColor Cyan
az storage cors add `
    --services b `
    --methods GET HEAD OPTIONS `
    --origins "*" `
    --allowed-headers "*" `
    --exposed-headers "*" `
    --max-age 3600 `
    --account-name $StorageAccount `
    --connection-string $storageConnection

if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: Failed to configure CORS (may already exist)" -ForegroundColor Yellow
} else {
    Write-Host "CORS configured successfully." -ForegroundColor Green
}

# Get the blob endpoint URL
Write-Host ""
Write-Host "Retrieving blob endpoint..." -ForegroundColor Cyan
$blobEndpoint = az storage account show `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --query "primaryEndpoints.blob" `
    --output tsv

# Assign Storage Blob Data Contributor role to the GitHub Actions service principal
Write-Host ""
Write-Host "Assigning Storage Blob Data Contributor role to GitHub Actions service principal..." -ForegroundColor Cyan

$storageAccountId = az storage account show `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --query id `
    --output tsv

$servicePrincipalName = "ai-newspaper-github-actions"
$spAppId = az ad sp list --display-name $servicePrincipalName --query "[0].appId" --output tsv

if ([string]::IsNullOrEmpty($spAppId)) {
    Write-Host "Warning: Service principal '$servicePrincipalName' not found." -ForegroundColor Yellow
    Write-Host "Please run setup-azure-resources.ps1 first or manually assign the role." -ForegroundColor Yellow
} else {
    az role assignment create `
        --assignee $spAppId `
        --role "Storage Blob Data Contributor" `
        --scope $storageAccountId `
        --output table 2>$null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Role assigned successfully." -ForegroundColor Green
    } else {
        Write-Host "Role may already be assigned or assignment failed." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Container Name: $ContainerName"
Write-Host "Public Access: blob (anonymous read access for blobs)"
Write-Host ""
Write-Host "Blob Endpoint: $blobEndpoint"
Write-Host "Container URL: $blobEndpoint$ContainerName/"
Write-Host ""
Write-Host "Files uploaded to this container will be publicly accessible at:"
Write-Host "  $blobEndpoint$ContainerName/<filename>" -ForegroundColor Cyan
Write-Host ""
Write-Host "==================================" -ForegroundColor Yellow
Write-Host "GitHub Workflow" -ForegroundColor Yellow
Write-Host "==================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "The deploy-public-files.yml workflow will automatically upload"
Write-Host "files from the public-files/ folder to this container when:"
Write-Host "  - Files in public-files/ are pushed to master"
Write-Host "  - Workflow is manually triggered"
Write-Host ""
Read-Host "Press Enter to exit"
