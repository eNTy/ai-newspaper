# Regenerate Service Principal for GitHub Actions
# Run this script when you need to update the AZURE_CREDENTIALS secret

$ResourceGroup = "ai-newspaper-rg"

Write-Host "=================================="
Write-Host "Regenerate Service Principal"
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
$selectedSub = az account show --query name -o tsv
Write-Host "Using subscription: $selectedSub" -ForegroundColor Green
Write-Host ""

# Get subscription ID
$subscriptionId = az account show --query id -o tsv

# Check if service principal already exists
Write-Host "Checking for existing service principal..." -ForegroundColor Cyan
$existingSp = az ad sp list --display-name "ai-newspaper-github-actions" --query "[0].appId" -o tsv 2>&1

if ($existingSp -and $LASTEXITCODE -eq 0) {
    Write-Host "Found existing service principal: $existingSp" -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host "Do you want to delete and recreate it? (y/n)"

    if ($confirm -eq "y" -or $confirm -eq "Y") {
        Write-Host "Deleting existing service principal..." -ForegroundColor Cyan
        az ad sp delete --id $existingSp

        if ($LASTEXITCODE -ne 0) {
            Write-Host "Warning: Failed to delete service principal, but continuing..." -ForegroundColor Yellow
        } else {
            Write-Host "Deleted successfully." -ForegroundColor Green
        }

        # Wait a bit for deletion to propagate
        Write-Host "Waiting for deletion to propagate..."
        Start-Sleep -Seconds 5
    } else {
        Write-Host "Cancelled by user." -ForegroundColor Yellow
        Read-Host "Press Enter to exit"
        exit 0
    }
}

# Create new Service Principal
Write-Host ""
Write-Host "Creating new Service Principal..." -ForegroundColor Cyan
$spOutput = az ad sp create-for-rbac `
    --name "ai-newspaper-github-actions" `
    --role contributor `
    --scopes "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup" `
    --sdk-auth

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Error: Failed to create Service Principal" -ForegroundColor Red
    Write-Host ""
    Write-Host "Common reasons:" -ForegroundColor Yellow
    Write-Host "1. You don't have permission to create service principals"
    Write-Host "2. The name 'ai-newspaper-github-actions' is still in use"
    Write-Host "3. Resource group doesn't exist"
    Write-Host ""
    Write-Host "Try running this command manually:" -ForegroundColor Cyan
    Write-Host "az ad sp create-for-rbac --name `"ai-newspaper-github-actions-v2`" --role contributor --scopes `"/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup`" --sdk-auth"
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Service Principal Created!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Update your GitHub secret:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Go to: https://github.com/YOUR_USERNAME/ai-newspaper/settings/secrets/actions" -ForegroundColor Cyan
Write-Host "2. Find the AZURE_CREDENTIALS secret" -ForegroundColor Cyan
Write-Host "3. Click 'Update' (or 'New repository secret' if it doesn't exist)" -ForegroundColor Cyan
Write-Host "4. Paste the following JSON as the value:" -ForegroundColor Cyan
Write-Host ""
Write-Host "==================================" -ForegroundColor Yellow
Write-Host $spOutput
Write-Host "==================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "IMPORTANT: Copy the JSON above!" -ForegroundColor Red
Write-Host ""
Read-Host "Press Enter to exit"
