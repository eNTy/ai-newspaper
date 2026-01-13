# Enable Application Insights for VideoGenerator Container App
# This provides structured logging with trace correlation and better filtering

param(
    [string]$ResourceGroup = "ai-newspaper-rg",
    [string]$ContainerAppName = "ai-newspaper-video-generator",
    [string]$AppInsightsName = "ai-newspaper-app-insights"
)

Write-Host "=== Enabling Application Insights for VideoGenerator ===" -ForegroundColor Cyan
Write-Host ""

# Check if Application Insights exists
Write-Host "Checking for Application Insights resource..." -ForegroundColor Yellow
$appInsights = az monitor app-insights component show `
    --app $AppInsightsName `
    --resource-group $ResourceGroup `
    2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating Application Insights resource..." -ForegroundColor Cyan
    az monitor app-insights component create `
        --app $AppInsightsName `
        --location westeurope `
        --resource-group $ResourceGroup `
        --application-type web `
        --output table

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to create Application Insights" -ForegroundColor Red
        exit 1
    }
    Write-Host "Application Insights created successfully." -ForegroundColor Green
} else {
    Write-Host "Application Insights already exists." -ForegroundColor Green
}

# Get Application Insights instrumentation key
Write-Host ""
Write-Host "Retrieving Application Insights connection string..." -ForegroundColor Yellow
$connectionString = az monitor app-insights component show `
    --app $AppInsightsName `
    --resource-group $ResourceGroup `
    --query "connectionString" `
    --output tsv

if ([string]::IsNullOrEmpty($connectionString)) {
    Write-Host "ERROR: Failed to retrieve connection string" -ForegroundColor Red
    exit 1
}

Write-Host "Connection string retrieved successfully." -ForegroundColor Green

# Update Container App with Application Insights
Write-Host ""
Write-Host "Updating Container App environment variables..." -ForegroundColor Cyan

# Get current storage connection string
$storageConnection = az storage account show-connection-string `
    --name ainewspaperstorage `
    --resource-group $ResourceGroup `
    --query connectionString `
    --output tsv

az containerapp update `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --set-env-vars `
        "AzureWebJobsStorage=$storageConnection" `
        "BLOB_CONTAINER_NAME=batch-runs" `
        "APPLICATIONINSIGHTS_CONNECTION_STRING=$connectionString" `
    --output table

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=== Setup Complete ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Application Insights has been enabled for VideoGenerator!" -ForegroundColor Green
    Write-Host ""
    Write-Host "To view logs:" -ForegroundColor Cyan
    Write-Host "1. Go to Azure Portal" -ForegroundColor Gray
    Write-Host "2. Navigate to Application Insights: $AppInsightsName" -ForegroundColor Gray
    Write-Host "3. Click 'Logs' under Monitoring" -ForegroundColor Gray
    Write-Host "4. Query traces table:" -ForegroundColor Gray
    Write-Host ""
    Write-Host "   traces" -ForegroundColor Yellow
    Write-Host "   | where cloud_RoleName == 'ai-newspaper-video-generator'" -ForegroundColor Yellow
    Write-Host "   | order by timestamp desc" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Note: You need to add Application Insights SDK to Program.cs for full integration." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "ERROR: Failed to update Container App" -ForegroundColor Red
}
