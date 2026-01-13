# Test VideoGenerator Container App API
param(
    [Parameter(Mandatory=$true)]
    [string]$StorageFolders,  # Comma-separated list of storage folders
    [string]$BaseUrl = "http://localhost:8080"
)

$folders = $StorageFolders -split ','

Write-Host "Testing VideoGenerator API" -ForegroundColor Cyan
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

# Test video generation endpoint
Write-Host "Testing video generation endpoint..." -ForegroundColor Cyan
$requestBody = @{
    storageFolders = $folders
} | ConvertTo-Json

Write-Host "Request body:" -ForegroundColor Yellow
Write-Host $requestBody
Write-Host ""

try {
    $response = Invoke-RestMethod `
        -Uri "$BaseUrl/api/generate" `
        -Method Post `
        -ContentType "application/json" `
        -Body $requestBody

    Write-Host "Video generation completed!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Results:" -ForegroundColor Cyan
    Write-Host ($response | ConvertTo-Json -Depth 10)
    Write-Host ""

    $successCount = ($response.results | Where-Object { $_.success -eq $true }).Count
    $totalCount = $response.results.Count

    Write-Host "Summary: $successCount/$totalCount videos generated successfully" -ForegroundColor $(if ($successCount -eq $totalCount) { "Green" } else { "Yellow" })

} catch {
    Write-Host "Video generation failed!" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red

    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response body: $responseBody" -ForegroundColor Red
    }

    exit 1
}
