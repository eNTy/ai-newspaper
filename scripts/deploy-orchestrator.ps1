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

    $rssUrl = az functionapp function show `
        -g $resourceGroup `
        -n "ai-newspaper-rss-processor" `
        --function-name "RssProcessor" `
        --query "invokeUrlTemplate" -o tsv

    $rssKey = az functionapp function keys list `
        -g $resourceGroup `
        -n "ai-newspaper-rss-processor" `
        --function-name "RssProcessor" `
        --query "default" -o tsv

    $simplifierUrl = az functionapp function show `
        -g $resourceGroup `
        -n "ai-newspaper-article-simplifier" `
        --function-name "ArticleSimplifier" `
        --query "invokeUrlTemplate" -o tsv

    $simplifierKey = az functionapp function keys list `
        -g $resourceGroup `
        -n "ai-newspaper-article-simplifier" `
        --function-name "ArticleSimplifier" `
        --query "default" -o tsv

    $imageUrl = az functionapp function show `
        -g $resourceGroup `
        -n "ai-newspaper-image-generator" `
        --function-name "ImageGenerator" `
        --query "invokeUrlTemplate" -o tsv

    $imageKey = az functionapp function keys list `
        -g $resourceGroup `
        -n "ai-newspaper-image-generator" `
        --function-name "ImageGenerator" `
        --query "default" -o tsv

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