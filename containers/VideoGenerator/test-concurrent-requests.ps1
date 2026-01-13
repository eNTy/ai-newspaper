# Test concurrent request handling for VideoGenerator
# This script sends multiple requests simultaneously to verify semaphore behavior

param(
    [string]$BaseUrl = "http://localhost:8080",
    [switch]$Azure,
    [string]$AzureResourceGroup = "ai-newspaper-rg",
    [string]$AzureContainerAppName = "ai-newspaper-video-generator"
)

if ($Azure) {
    Write-Host "Getting Azure Container App URL..." -ForegroundColor Cyan
    $fqdn = az containerapp show `
        --name $AzureContainerAppName `
        --resource-group $AzureResourceGroup `
        --query "properties.configuration.ingress.fqdn" `
        --output tsv

    if ([string]::IsNullOrEmpty($fqdn)) {
        Write-Host "ERROR: Could not retrieve Container App URL" -ForegroundColor Red
        exit 1
    }

    $BaseUrl = "https://$fqdn"
}

Write-Host "=== Testing Concurrent Requests ===" -ForegroundColor Cyan
Write-Host "Target: $BaseUrl" -ForegroundColor Yellow
Write-Host ""

# Test data - simulate multiple concurrent requests
$request1 = @{
    storageFolders = @("test-folder-1", "test-folder-2")
} | ConvertTo-Json

$request2 = @{
    storageFolders = @("test-folder-3")
} | ConvertTo-Json

$request3 = @{
    storageFolders = @("test-folder-4", "test-folder-5")
} | ConvertTo-Json

Write-Host "Sending 3 concurrent requests..." -ForegroundColor Cyan
Write-Host "Request 1: 2 folders" -ForegroundColor Gray
Write-Host "Request 2: 1 folder" -ForegroundColor Gray
Write-Host "Request 3: 2 folders" -ForegroundColor Gray
Write-Host ""

$startTime = Get-Date

# Create script blocks for parallel execution
$scriptBlock1 = {
    param($url, $body)
    $result = @{
        RequestId = 1
        StartTime = Get-Date
    }
    try {
        $response = Invoke-RestMethod `
            -Uri "$url/api/generate" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec 300
        $result.Success = $true
        $result.Response = $response
    }
    catch {
        $result.Success = $false
        $result.Error = $_.Exception.Message
    }
    $result.EndTime = Get-Date
    $result.Duration = ($result.EndTime - $result.StartTime).TotalSeconds
    return $result
}

$scriptBlock2 = {
    param($url, $body)
    $result = @{
        RequestId = 2
        StartTime = Get-Date
    }
    try {
        $response = Invoke-RestMethod `
            -Uri "$url/api/generate" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec 300
        $result.Success = $true
        $result.Response = $response
    }
    catch {
        $result.Success = $false
        $result.Error = $_.Exception.Message
    }
    $result.EndTime = Get-Date
    $result.Duration = ($result.EndTime - $result.StartTime).TotalSeconds
    return $result
}

$scriptBlock3 = {
    param($url, $body)
    $result = @{
        RequestId = 3
        StartTime = Get-Date
    }
    try {
        $response = Invoke-RestMethod `
            -Uri "$url/api/generate" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec 300
        $result.Success = $true
        $result.Response = $response
    }
    catch {
        $result.Success = $false
        $result.Error = $_.Exception.Message
    }
    $result.EndTime = Get-Date
    $result.Duration = ($result.EndTime - $result.StartTime).TotalSeconds
    return $result
}

# Start parallel jobs
Write-Host "Starting parallel requests..." -ForegroundColor Yellow
$job1 = Start-Job -ScriptBlock $scriptBlock1 -ArgumentList $BaseUrl, $request1
$job2 = Start-Job -ScriptBlock $scriptBlock2 -ArgumentList $BaseUrl, $request2
$job3 = Start-Job -ScriptBlock $scriptBlock3 -ArgumentList $BaseUrl, $request3

# Wait for all jobs to complete
Write-Host "Waiting for requests to complete..." -ForegroundColor Yellow
$jobs = @($job1, $job2, $job3)
$jobs | Wait-Job | Out-Null

$endTime = Get-Date
$totalDuration = ($endTime - $startTime).TotalSeconds

Write-Host ""
Write-Host "=== Results ===" -ForegroundColor Cyan

# Get results from jobs
$result1 = Receive-Job -Job $job1
$result2 = Receive-Job -Job $job2
$result3 = Receive-Job -Job $job3

# Clean up jobs
$jobs | Remove-Job

# Display results
Write-Host ""
Write-Host "Request 1:" -ForegroundColor Yellow
Write-Host "  Duration: $([math]::Round($result1.Duration, 2))s" -ForegroundColor Gray
if ($result1.Success) {
    Write-Host "  Status: SUCCESS" -ForegroundColor Green
    Write-Host "  Response: $($result1.Response | ConvertTo-Json -Depth 2)" -ForegroundColor Gray
} else {
    Write-Host "  Status: FAILED" -ForegroundColor Red
    Write-Host "  Error: $($result1.Error)" -ForegroundColor Red
}

Write-Host ""
Write-Host "Request 2:" -ForegroundColor Yellow
Write-Host "  Duration: $([math]::Round($result2.Duration, 2))s" -ForegroundColor Gray
if ($result2.Success) {
    Write-Host "  Status: SUCCESS" -ForegroundColor Green
    Write-Host "  Response: $($result2.Response | ConvertTo-Json -Depth 2)" -ForegroundColor Gray
} else {
    Write-Host "  Status: FAILED" -ForegroundColor Red
    Write-Host "  Error: $($result2.Error)" -ForegroundColor Red
}

Write-Host ""
Write-Host "Request 3:" -ForegroundColor Yellow
Write-Host "  Duration: $([math]::Round($result3.Duration, 2))s" -ForegroundColor Gray
if ($result3.Success) {
    Write-Host "  Status: SUCCESS" -ForegroundColor Green
    Write-Host "  Response: $($result3.Response | ConvertTo-Json -Depth 2)" -ForegroundColor Gray
} else {
    Write-Host "  Status: FAILED" -ForegroundColor Red
    Write-Host "  Error: $($result3.Error)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Total test duration: $([math]::Round($totalDuration, 2))s" -ForegroundColor Yellow
Write-Host ""
Write-Host "Expected behavior:" -ForegroundColor Gray
Write-Host "  - Requests should queue up and process sequentially" -ForegroundColor Gray
Write-Host "  - Only one video generation happens at a time (per instance)" -ForegroundColor Gray
Write-Host "  - Total duration ≈ sum of individual request durations" -ForegroundColor Gray
Write-Host ""
Write-Host "To test against Azure:" -ForegroundColor Cyan
Write-Host "  .\test-concurrent-requests.ps1 -Azure" -ForegroundColor Gray
