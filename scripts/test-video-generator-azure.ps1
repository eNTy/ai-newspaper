# Test VideoGenerator Container App in Azure
# This script helps diagnose and test the VideoGenerator Container App

param(
    [string]$ResourceGroup = "ai-newspaper-rg",
    [string]$ContainerAppName = "ai-newspaper-video-generator",
    [switch]$ShowLogs,
    [switch]$ShowConfig,
    [switch]$TestHealth,
    [switch]$TestGenerate,
    [string]$StorageFolder
)

Write-Host "=== VideoGenerator Container App Diagnostics ===" -ForegroundColor Cyan

# 1. Check Container App Status
Write-Host "`n1. Container App Status:" -ForegroundColor Yellow
az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "{name:name, status:properties.runningStatus, replicas:properties.template.scale, ingress:properties.configuration.ingress.fqdn}" `
    --output table

# 2. Show Configuration (if requested)
if ($ShowConfig) {
    Write-Host "`n2. Container App Configuration:" -ForegroundColor Yellow
    az containerapp show `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --query "properties.configuration" `
        --output json

    Write-Host "`nEnvironment Variables:" -ForegroundColor Yellow
    az containerapp show `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --query "properties.template.containers[0].env" `
        --output table
}

# 3. Get Container App Logs (if requested)
if ($ShowLogs) {
    Write-Host "`n3. Recent Container Logs:" -ForegroundColor Yellow
    Write-Host "Fetching logs from Log Analytics..." -ForegroundColor Gray

    # Get the latest replica logs
    az containerapp logs show `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --tail 50 `
        --follow $false
}

# 4. Get the Container App URL
Write-Host "`n4. Getting Container App URL..." -ForegroundColor Yellow
$appUrl = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.configuration.ingress.fqdn" `
    --output tsv

if ([string]::IsNullOrEmpty($appUrl)) {
    Write-Host "ERROR: Could not retrieve Container App URL" -ForegroundColor Red
    exit 1
}

$baseUrl = "https://$appUrl"
Write-Host "Container App URL: $baseUrl" -ForegroundColor Green
Write-Host "Health endpoint: $baseUrl/health" -ForegroundColor Green
Write-Host "Generate endpoint: $baseUrl/api/generate" -ForegroundColor Green

# 5. Test Health Endpoint (if requested)
if ($TestHealth) {
    Write-Host "`n5. Testing Health Endpoint..." -ForegroundColor Yellow
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get -TimeoutSec 30
        Write-Host "Health check PASSED: $($response | ConvertTo-Json)" -ForegroundColor Green
    }
    catch {
        Write-Host "Health check FAILED: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
        Write-Host "This might indicate the container is not starting properly." -ForegroundColor Yellow
    }
}

# 6. Test Generate Endpoint (if requested)
if ($TestGenerate -and $StorageFolder) {
    Write-Host "`n6. Testing Generate Endpoint..." -ForegroundColor Yellow
    $body = @{
        storageFolders = @($StorageFolder)
    } | ConvertTo-Json

    Write-Host "Request body: $body" -ForegroundColor Gray

    try {
        $response = Invoke-RestMethod `
            -Uri "$baseUrl/api/generate" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec 300

        Write-Host "Generate request SUCCEEDED:" -ForegroundColor Green
        Write-Host ($response | ConvertTo-Json -Depth 10) -ForegroundColor Green
    }
    catch {
        Write-Host "Generate request FAILED: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) {
            Write-Host "Error details: $($_.ErrorDetails.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "`n=== Diagnostics Complete ===" -ForegroundColor Cyan
Write-Host "`nUsage examples:" -ForegroundColor Yellow
Write-Host "  .\test-video-generator-azure.ps1 -ShowLogs          # View recent logs"
Write-Host "  .\test-video-generator-azure.ps1 -ShowConfig        # View configuration"
Write-Host "  .\test-video-generator-azure.ps1 -TestHealth        # Test health endpoint"
Write-Host "  .\test-video-generator-azure.ps1 -TestGenerate -StorageFolder 'age-8/2024-01-13/article-0'"
Write-Host "`nTo view live logs in Azure Portal:"
Write-Host "  1. Go to the Container App in Azure Portal"
Write-Host "  2. Navigate to 'Logs' under Monitoring"
Write-Host "  3. Run query: ContainerAppConsoleLogs_CL | order by TimeGenerated desc | take 100"
