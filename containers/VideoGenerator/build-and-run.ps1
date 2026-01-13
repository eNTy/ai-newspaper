# Build and run VideoGenerator Container App locally
param(
    [switch]$Build,
    [switch]$Run,
    [switch]$Stop
)

$ImageName = "videogenerator"
$ContainerName = "videogenerator-local"
$Port = 8080

if ($Stop) {
    Write-Host "Stopping and removing container..." -ForegroundColor Cyan
    docker stop $ContainerName 2>$null
    docker rm $ContainerName 2>$null
    Write-Host "Container stopped and removed." -ForegroundColor Green
    exit 0
}

if ($Build) {
    Write-Host "Building Docker image..." -ForegroundColor Cyan
    docker build -t $ImageName .
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Docker build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Docker image built successfully." -ForegroundColor Green
}

if ($Run) {
    # Stop existing container if running
    docker stop $ContainerName 2>$null
    docker rm $ContainerName 2>$null

    Write-Host "Starting Docker container..." -ForegroundColor Cyan
    Write-Host "API will be available at: http://localhost:$Port" -ForegroundColor Yellow
    Write-Host "Health check: http://localhost:$Port/health" -ForegroundColor Yellow
    Write-Host "Generate endpoint: POST http://localhost:$Port/api/generate" -ForegroundColor Yellow
    Write-Host ""

    # Use host.docker.internal to connect to Azurite running on host
    docker run -d `
        --name $ContainerName `
        -p ${Port}:8080 `
        -e AzureWebJobsStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1;" `
        -e BLOB_CONTAINER_NAME="batch-runs" `
        $ImageName

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to start container!" -ForegroundColor Red
        exit 1
    }

    Write-Host "Container started successfully." -ForegroundColor Green
    Write-Host ""
    Write-Host "View logs with: docker logs -f $ContainerName" -ForegroundColor Cyan
    Write-Host "Stop with: docker stop $ContainerName" -ForegroundColor Cyan
}

if (-not $Build -and -not $Run -and -not $Stop) {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\build-and-run.ps1 -Build          Build Docker image" -ForegroundColor Cyan
    Write-Host "  .\build-and-run.ps1 -Run            Run Docker container" -ForegroundColor Cyan
    Write-Host "  .\build-and-run.ps1 -Build -Run     Build and run" -ForegroundColor Cyan
    Write-Host "  .\build-and-run.ps1 -Stop           Stop and remove container" -ForegroundColor Cyan
}
