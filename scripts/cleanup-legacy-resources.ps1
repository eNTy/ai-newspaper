# One-time cleanup script to delete legacy Azure Function Apps
# These were consolidated into the NewspaperOrchestrator Function App
# Safe to run - only deletes the 5 legacy function apps and orphaned app service plans

$ResourceGroup = "ai-newspaper-rg"

$LegacyFunctionApps = @(
    "ai-newspaper-rss-processor",
    "ai-newspaper-article-simplifier",
    "ai-newspaper-image-generator",
    "ai-newspaper-text-to-speech",
    "ai-newspaper-instagram-publisher"
)

Write-Host "==================================" -ForegroundColor Yellow
Write-Host "Cleanup Legacy Azure Resources" -ForegroundColor Yellow
Write-Host "==================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "This will delete the following resources from '$ResourceGroup':" -ForegroundColor Cyan
foreach ($app in $LegacyFunctionApps) {
    Write-Host "  - Function App: $app" -ForegroundColor Cyan
}
Write-Host "  - Any orphaned App Service Plans" -ForegroundColor Cyan
Write-Host ""
Write-Host "Application Insights will be kept (used by Orchestrator and VideoGenerator)." -ForegroundColor Green

$confirm = Read-Host "Are you sure? (y/N)"
if ($confirm -ne "y") {
    Write-Host "Aborted." -ForegroundColor Yellow
    exit 0
}

Write-Host ""

foreach ($app in $LegacyFunctionApps) {
    Write-Host "Deleting Function App: $app..." -ForegroundColor Cyan
    $null = az functionapp show --name $app --resource-group $ResourceGroup 2>&1
    if ($LASTEXITCODE -eq 0) {
        az functionapp delete --name $app --resource-group $ResourceGroup
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Deleted." -ForegroundColor Green
        } else {
            Write-Host "  Error deleting $app" -ForegroundColor Red
        }
    } else {
        Write-Host "  Not found, skipping." -ForegroundColor Yellow
    }
}

# Clean up orphaned App Service Plans (legacy function apps may have created them)
Write-Host ""
Write-Host "Checking for orphaned App Service Plans..." -ForegroundColor Cyan
$plans = az appservice plan list --resource-group $ResourceGroup --query "[?numberOfSites==``0``].name" -o tsv
if (-not [string]::IsNullOrEmpty($plans)) {
    foreach ($plan in $plans -split "`n") {
        $plan = $plan.Trim()
        if (-not [string]::IsNullOrEmpty($plan)) {
            Write-Host "Deleting empty App Service Plan: $plan..." -ForegroundColor Cyan
            az appservice plan delete --name $plan --resource-group $ResourceGroup --yes
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Deleted." -ForegroundColor Green
            } else {
                Write-Host "  Error deleting plan $plan" -ForegroundColor Red
            }
        }
    }
} else {
    Write-Host "  No orphaned plans found." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Green
Write-Host "Cleanup complete!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host ""
Write-Host "Remaining resources in '$ResourceGroup':" -ForegroundColor Cyan
Write-Host "  - ai-newspaper-orchestrator (Function App)"
Write-Host "  - ai-newspaper-video-generator (Container App)"
Write-Host "  - ai-newspaper-app-insights (Application Insights)"
Write-Host "  - ainewspaperstorage (Storage Account)"
Write-Host "  - ainewspapervideogen (Container Registry)"
Write-Host ""
