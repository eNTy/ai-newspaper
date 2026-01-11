# Deploy NewspaperOrchestrator to Azure
# Usage: .\deploy-orchestrator.ps1

$ErrorActionPreference = "Stop"

$resourceGroup = "ai-newspaper-rg"
$functionAppName = "ai-newspaper-orchestrator"
$projectPath = "..\lambdas\NewspaperOrchestrator"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deploying NewspaperOrchestrator to Azure" -ForegroundColor Cyan
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

    # Get function URLs and keys for other functions
    Write-Host "`nRetrieving function URLs and keys..." -ForegroundColor Yellow
    Write-Host "(Functions may take a moment to become available...)" -ForegroundColor Gray

    # Wait a bit for functions to stabilize
    Start-Sleep -Seconds 10

    # Helper function to retry getting function info
    function Get-FunctionInfo {
        param($AppName, $FunctionName, $MaxRetries = 5)

        for ($i = 1; $i -le $MaxRetries; $i++) {
            try {
                $url = az functionapp function show `
                    -g $resourceGroup `
                    -n $AppName `
                    --function-name $FunctionName `
                    --query "invokeUrlTemplate" -o tsv 2>$null

                if ($url) {
                    $key = az functionapp function keys list `
                        -g $resourceGroup `
                        -n $AppName `
                        --function-name $FunctionName `
                        --query "default" -o tsv 2>$null

                    if ($key) {
                        return @{ Url = $url; Key = $key }
                    }
                }
            }
            catch {
                # Ignore errors and retry
            }

            if ($i -lt $MaxRetries) {
                Write-Host "  Retry $i/$MaxRetries for $FunctionName..." -ForegroundColor Gray
                Start-Sleep -Seconds 10
            }
        }

        throw "Failed to retrieve function info for $FunctionName after $MaxRetries attempts"
    }

    # Get RssProcessor
    Write-Host "  Getting RssProcessor..." -ForegroundColor Gray
    $rssInfo = Get-FunctionInfo -AppName "ai-newspaper-rss-processor" -FunctionName "RssProcessor"
    $rssUrl = $rssInfo.Url
    $rssKey = $rssInfo.Key

    # Get ArticleSimplifier
    Write-Host "  Getting ArticleSimplifier..." -ForegroundColor Gray
    $simplifierInfo = Get-FunctionInfo -AppName "ai-newspaper-article-simplifier" -FunctionName "ArticleSimplifier"
    $simplifierUrl = $simplifierInfo.Url
    $simplifierKey = $simplifierInfo.Key

    # Get ImageGenerator
    Write-Host "  Getting ImageGenerator..." -ForegroundColor Gray
    $imageInfo = Get-FunctionInfo -AppName "ai-newspaper-image-generator" -FunctionName "ImageGenerator"
    $imageUrl = $imageInfo.Url
    $imageKey = $imageInfo.Key

    Write-Host "  Successfully retrieved all function info!" -ForegroundColor Green

    # Configure app settings with function URLs including keys
    Write-Host "`nConfiguring app settings..." -ForegroundColor Yellow
    az functionapp config appsettings set `
        --name $functionAppName `
        --resource-group $resourceGroup `
        --settings `
            "RSS_PROCESSOR_URL=${rssUrl}?code=${rssKey}" `
            "ARTICLE_SIMPLIFIER_URL=${simplifierUrl}?code=${simplifierKey}" `
            "IMAGE_GENERATOR_URL=${imageUrl}?code=${imageKey}" `
            "BLOB_CONTAINER_NAME=batch-runs" `
            "DEFAULT_RSS_URL=https://ct24.ceskatelevize.cz/rss/tema/vyber-redakce-84313"

    if ($LASTEXITCODE -ne 0) { throw "Configuration failed" }

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "Deployment successful!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "`nFunction URLs configured:" -ForegroundColor Cyan
    Write-Host "  RSS Processor: $rssUrl" -ForegroundColor Gray
    Write-Host "  Article Simplifier: $simplifierUrl" -ForegroundColor Gray
    Write-Host "  Image Generator: $imageUrl" -ForegroundColor Gray
    Write-Host "`nOrchestrator URL: https://$functionAppName.azurewebsites.net/api/StartNewspaperBatch" -ForegroundColor Cyan
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