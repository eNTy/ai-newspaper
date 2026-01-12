# PowerShell script to test Docker build locally
# Run this when Docker Desktop is available on your machine

Write-Host "Testing Docker build for VideoGenerator..." -ForegroundColor Green
Write-Host ""

# Check if Docker is available
try {
    docker --version | Out-Null
    Write-Host "Docker is available" -ForegroundColor Green
}
catch {
    Write-Host "Error: Docker is not available. Please install Docker Desktop." -ForegroundColor Red
    Write-Host "Download from: https://www.docker.com/products/docker-desktop" -ForegroundColor Yellow
    exit 1
}

# Check if Docker daemon is running
try {
    docker ps | Out-Null
    Write-Host "Docker daemon is running" -ForegroundColor Green
}
catch {
    Write-Host "Error: Docker daemon is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Building Docker image..." -ForegroundColor Cyan
Write-Host "This may take 5-10 minutes on first build..." -ForegroundColor Yellow
Write-Host ""

$startTime = Get-Date

# Build the image
docker build -t videogenerator:test .

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Docker build FAILED!" -ForegroundColor Red
    exit 1
}

$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host "Docker build SUCCEEDED!" -ForegroundColor Green
Write-Host "Build time: $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
Write-Host ""

# Verify FFMPEG is in the image
Write-Host "Verifying FFMPEG installation in image..." -ForegroundColor Cyan
docker run --rm videogenerator:test ffmpeg -version

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "FFMPEG verification SUCCESSFUL!" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "FFMPEG verification FAILED!" -ForegroundColor Red
}

Write-Host ""
Write-Host "Image details:" -ForegroundColor Yellow
docker images videogenerator:test

Write-Host ""
Write-Host "To run the container, use:" -ForegroundColor Yellow
Write-Host "  docker run -p 7076:80 -e AzureWebJobsStorage=`"UseDevelopmentStorage=true`" -e BLOB_CONTAINER_NAME=`"batch-runs`" videogenerator:test" -ForegroundColor Cyan
