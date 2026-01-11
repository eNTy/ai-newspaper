# Deploy RssProcessor to Azure
# Usage: .\deploy-rss-processor.ps1

$ErrorActionPreference = "Stop"

$resourceGroup = "ai-newspaper-rg"
$functionAppName = "ai-newspaper-rss-processor"
$projectPath = "..\lambdas\RssProcessor"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deploying RssProcessor to Azure" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Build and publish
Write-Host "`nBuilding project..." -ForegroundColor Yellow
Push-Location $projectPath
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    dotnet publish --configuration Release --output ./output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Write-Host "Build successful!" -ForegroundColor Green

    # Create zip package
    Write-Host "`nCreating deployment package..." -ForegroundColor Yellow
    $zipPath = Join-Path $PWD "deploy.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Push-Location output
    Compress-Archive -Path * -DestinationPath ..\deploy.zip
    Pop-Location

    Write-Host "Package created: $zipPath" -ForegroundColor Green

    # Deploy to Azure
    Write-Host "`nDeploying to Azure..." -ForegroundColor Yellow
    az functionapp deployment source config-zip `
        --resource-group $resourceGroup `
        --name $functionAppName `
        --src deploy.zip

    if ($LASTEXITCODE -ne 0) { throw "Azure deployment failed" }

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "Deployment successful!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "`nFunction URL: https://$functionAppName.azurewebsites.net/api/RssProcessor" -ForegroundColor Cyan
}
catch {
    Write-Host "`n========================================" -ForegroundColor Red
    Write-Host "Deployment failed: $_" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}