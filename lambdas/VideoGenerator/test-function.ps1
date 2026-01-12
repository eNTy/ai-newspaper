# PowerShell script to test the VideoGenerator function

param(
    [string]$StorageFolder = "batch-runs/test",
    [string]$FunctionUrl = "http://localhost:7076/api/VideoGenerator",
    [string]$FunctionKey = ""
)

Write-Host "Testing VideoGenerator function..." -ForegroundColor Green
Write-Host "Storage Folder: $StorageFolder" -ForegroundColor Cyan
Write-Host "Function URL: $FunctionUrl" -ForegroundColor Cyan

if ($FunctionKey) {
    Write-Host "Using function key authentication" -ForegroundColor Gray
} else {
    Write-Host "No function key provided (Anonymous or local dev)" -ForegroundColor Gray
}
Write-Host ""

$body = @{
    storageFolder = $StorageFolder
} | ConvertTo-Json

Write-Host "Request body:" -ForegroundColor Yellow
Write-Host $body -ForegroundColor White
Write-Host ""

try {
    # Build URL with key if provided
    $url = $FunctionUrl
    if ($FunctionKey) {
        $url = "$FunctionUrl`?code=$FunctionKey"
    }

    Write-Host "Sending request..." -ForegroundColor Gray
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json"

    Write-Host ""
    Write-Host "Success!" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Yellow
    $response | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor White

    if ($response.videoUrl) {
        Write-Host ""
        Write-Host "Video URL: $($response.videoUrl)" -ForegroundColor Cyan
    }

    if ($response.storageFolder) {
        Write-Host "Storage Folder: $($response.storageFolder)" -ForegroundColor Cyan
    }
}
catch {
    Write-Host ""
    Write-Host "Error!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red

    # Special handling for 401 Unauthorized
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host ""
        Write-Host "TROUBLESHOOTING 401 Unauthorized:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Option 1: Change authorization level to Anonymous (Recommended for local dev)" -ForegroundColor Cyan
        Write-Host "  1. Edit VideoGeneratorFunction.cs" -ForegroundColor Gray
        Write-Host "  2. Change: AuthorizationLevel.Function" -ForegroundColor Gray
        Write-Host "  3. To:     AuthorizationLevel.Anonymous" -ForegroundColor Gray
        Write-Host "  4. Rebuild: .\build-and-run.ps1 -Run" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Option 2: Get the function key from container" -ForegroundColor Cyan
        Write-Host "  docker exec videogen cat /azure-functions-host/secrets/host.json" -ForegroundColor Gray
        Write-Host "  Then use: .\test-function.ps1 -FunctionKey `"your-key`"" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Option 3: Use the master key (for local development)" -ForegroundColor Cyan
        Write-Host "  Default local master key is often available in container" -ForegroundColor Gray
    }

    if ($_.Exception.Response) {
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            if ($responseBody) {
                Write-Host ""
                Write-Host "Response body:" -ForegroundColor Yellow
                Write-Host $responseBody -ForegroundColor White
            }
        }
        catch {
            # Ignore if we can't read response
        }
    }

    Write-Host ""
    Write-Host "Quick fixes:" -ForegroundColor Yellow
    Write-Host "- Make sure container is running: docker ps | findstr videogen" -ForegroundColor Gray
    Write-Host "- Check container logs: .\build-and-run.ps1 -Logs" -ForegroundColor Gray
    Write-Host "- Verify Azurite is running: netstat -an | findstr 10000" -ForegroundColor Gray
    Write-Host "- Try with curl: curl -X POST http://localhost:7076/api/VideoGenerator -d '{`"storageFolder`":`"test`"}' -H `"Content-Type: application/json`"" -ForegroundColor Gray
}
