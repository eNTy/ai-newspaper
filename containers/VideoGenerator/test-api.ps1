# Test VideoGenerator Container App API (Async Pattern)
param(
    [Parameter(Mandatory=$true)]
    [string]$StorageFolders,  # Comma-separated list of storage folders
    [string]$BaseUrl = "http://localhost:8080",
    [int]$MaxPollAttempts = 60,
    [int]$PollIntervalSeconds = 5
)

$folders = $StorageFolders -split ','

Write-Host "Testing VideoGenerator API (Async Pattern)" -ForegroundColor Cyan
Write-Host "Base URL: $BaseUrl" -ForegroundColor Yellow
Write-Host "Storage Folders: $($folders -join ', ')" -ForegroundColor Yellow
Write-Host ""

# Test health endpoint
Write-Host "Testing health endpoint..." -ForegroundColor Cyan
try {
    $healthResponse = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get
    Write-Host "Health check passed:" -ForegroundColor Green
    Write-Host ($healthResponse | ConvertTo-Json)
    Write-Host ""
} catch {
    Write-Host "Health check failed: $_" -ForegroundColor Red
    exit 1
}

# Step 1: Trigger async video generation
Write-Host "Step 1: Triggering async video generation..." -ForegroundColor Cyan
$requestBody = @{
    storageFolders = $folders
} | ConvertTo-Json

Write-Host "Request body:" -ForegroundColor Yellow
Write-Host $requestBody
Write-Host ""

try {
    $triggerResponse = Invoke-RestMethod `
        -Uri "$BaseUrl/api/generate" `
        -Method Post `
        -ContentType "application/json" `
        -Body $requestBody

    Write-Host "Video generation job triggered!" -ForegroundColor Green
    Write-Host "Job ID: $($triggerResponse.jobId)" -ForegroundColor Yellow
    Write-Host "Status: $($triggerResponse.status)" -ForegroundColor Yellow
    Write-Host "Message: $($triggerResponse.message)" -ForegroundColor Yellow
    Write-Host ""

    $jobId = $triggerResponse.jobId

} catch {
    Write-Host "Failed to trigger video generation!" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red

    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response body: $responseBody" -ForegroundColor Red
    }

    exit 1
}

# Step 2: Poll for completion
Write-Host "Step 2: Polling for completion..." -ForegroundColor Cyan
$attempt = 0
$completed = $false
$statusResponse = $null

while ($attempt -lt $MaxPollAttempts -and -not $completed) {
    $attempt++

    Write-Host "Poll attempt $attempt/$MaxPollAttempts (waiting $PollIntervalSeconds seconds)..." -ForegroundColor Yellow
    Start-Sleep -Seconds $PollIntervalSeconds

    try {
        $statusResponse = Invoke-RestMethod `
            -Uri "$BaseUrl/api/generate/status/$jobId" `
            -Method Get

        $status = $statusResponse.status
        $processed = $statusResponse.processedFolders
        $total = $statusResponse.totalFolders

        Write-Host "  Status: $status | Progress: $processed/$total folders" -ForegroundColor Cyan

        if ($status -eq "Completed" -or $status -eq "Failed") {
            $completed = $true
        }

    } catch {
        Write-Host "  Failed to check status: $_" -ForegroundColor Red

        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "  Job not found. It may have expired." -ForegroundColor Red
            exit 1
        }
    }
}

Write-Host ""

# Step 3: Display final results
if (-not $completed) {
    Write-Host "Video generation timed out after $MaxPollAttempts attempts" -ForegroundColor Red
    Write-Host "Last known status: $($statusResponse.status)" -ForegroundColor Yellow
    Write-Host "Progress: $($statusResponse.processedFolders)/$($statusResponse.totalFolders)" -ForegroundColor Yellow
    exit 1
}

Write-Host "Video generation completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Final Status:" -ForegroundColor Cyan
Write-Host "  Job ID: $($statusResponse.jobId)" -ForegroundColor White
Write-Host "  Status: $($statusResponse.status)" -ForegroundColor $(if ($statusResponse.status -eq "Completed") { "Green" } else { "Red" })
Write-Host "  Created At: $($statusResponse.createdAt)" -ForegroundColor White
Write-Host "  Completed At: $($statusResponse.completedAt)" -ForegroundColor White
Write-Host "  Total Folders: $($statusResponse.totalFolders)" -ForegroundColor White
Write-Host "  Processed Folders: $($statusResponse.processedFolders)" -ForegroundColor White
Write-Host ""

if ($statusResponse.status -eq "Failed") {
    Write-Host "Error: $($statusResponse.errorMessage)" -ForegroundColor Red
    exit 1
}

Write-Host "Results:" -ForegroundColor Cyan
Write-Host ($statusResponse.results | ConvertTo-Json -Depth 10)
Write-Host ""

$successCount = ($statusResponse.results | Where-Object { $_.success -eq $true }).Count
$totalCount = $statusResponse.results.Count

Write-Host "Summary: $successCount/$totalCount videos generated successfully" -ForegroundColor $(if ($successCount -eq $totalCount) { "Green" } else { "Yellow" })

# Display any failures
$failures = $statusResponse.results | Where-Object { $_.success -eq $false }
if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed videos:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $($failure.folder): $($failure.error)" -ForegroundColor Red
    }
}

if ($successCount -ne $totalCount) {
    exit 1
}
