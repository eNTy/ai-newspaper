# PowerShell script to build and run the VideoGenerator container
param(
    [switch]$Run,
    [switch]$NoBuild,
    [switch]$Stop,
    [switch]$Logs,
    [switch]$Clean
)

$imageName = "videogenerator:local"
$containerName = "videogen"

# Stop container
if ($Stop) {
    Write-Host "Stopping VideoGenerator container..." -ForegroundColor Yellow
    docker stop $containerName 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Container stopped successfully" -ForegroundColor Green
    } else {
        Write-Host "Container was not running" -ForegroundColor Gray
    }
    exit 0
}

# Show logs
if ($Logs) {
    Write-Host "Showing VideoGenerator container logs..." -ForegroundColor Cyan
    Write-Host "(Press Ctrl+C to exit)" -ForegroundColor Gray
    docker logs -f $containerName
    exit 0
}

# Clean up
if ($Clean) {
    Write-Host "Cleaning up VideoGenerator resources..." -ForegroundColor Yellow

    # Stop and remove container
    docker stop $containerName 2>$null
    docker rm $containerName 2>$null

    # Remove image
    docker rmi $imageName 2>$null

    Write-Host "Cleanup complete" -ForegroundColor Green
    exit 0
}

# Build Docker image (unless -NoBuild is specified)
if (-not $NoBuild) {
    Write-Host "Building VideoGenerator Docker image..." -ForegroundColor Green
    Write-Host "(This may take 5-10 minutes on first build)" -ForegroundColor Gray
    Write-Host ""

    docker build -t $imageName .

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Docker build FAILED!" -ForegroundColor Red
        Write-Host "Check the error messages above" -ForegroundColor Yellow
        exit 1
    }

    Write-Host ""
    Write-Host "Docker image built successfully!" -ForegroundColor Green
} else {
    Write-Host "Skipping build (using existing image)" -ForegroundColor Gray
}

# Run container if -Run flag is provided
if ($Run) {
    Write-Host ""
    Write-Host "Starting VideoGenerator container..." -ForegroundColor Green

    # Stop existing container if running
    docker stop $containerName 2>$null
    docker rm $containerName 2>$null

    # Check if Azurite is running
    Write-Host "Checking for Azurite..." -ForegroundColor Gray
    $azuritePort = netstat -an | Select-String "10000.*LISTENING"

    if (-not $azuritePort) {
        Write-Host ""
        Write-Host "WARNING: Azurite is not running!" -ForegroundColor Yellow
        Write-Host "Please start Azurite in a separate terminal:" -ForegroundColor Yellow
        Write-Host "  azurite" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Or use the VS Code task: Run Task -> start azurite" -ForegroundColor Yellow
        Write-Host ""

        $response = Read-Host "Continue anyway? (y/N)"
        if ($response -ne "y" -and $response -ne "Y") {
            Write-Host "Aborted" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "Azurite is running" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Container starting on port 7076..." -ForegroundColor Cyan
    Write-Host "Function URL: http://localhost:7076/api/VideoGenerator" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Press Ctrl+C to stop the container" -ForegroundColor Gray
    Write-Host ""

    # Run with --rm to auto-remove on stop
    # Use host.docker.internal to access host machine's Azurite
    docker run --rm `
        --name $containerName `
        -p 7076:80 `
        -e AzureWebJobsStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1;QueueEndpoint=http://host.docker.internal:10001/devstoreaccount1;TableEndpoint=http://host.docker.internal:10002/devstoreaccount1;" `
        -e BLOB_CONTAINER_NAME="batch-runs" `
        $imageName

} else {
    # Just built, show usage instructions
    Write-Host ""
    Write-Host "To run the container:" -ForegroundColor Yellow
    Write-Host "  .\build-and-run.ps1 -Run" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Note: Azurite will be started automatically if not running" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Or run directly with Docker:" -ForegroundColor Yellow
    Write-Host "  docker run --rm -p 7076:80 \" -ForegroundColor Cyan
    Write-Host "    -e AzureWebJobsStorage=`"UseDevelopmentStorage=true`" \" -ForegroundColor Cyan
    Write-Host "    -e BLOB_CONTAINER_NAME=`"batch-runs`" \" -ForegroundColor Cyan
    Write-Host "    --name videogen \" -ForegroundColor Cyan
    Write-Host "    videogenerator:local" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Quick commands:" -ForegroundColor Yellow
    Write-Host "  .\build-and-run.ps1 -Run -NoBuild    # Run without rebuilding" -ForegroundColor Gray
    Write-Host "  .\build-and-run.ps1 -Logs             # View container logs" -ForegroundColor Gray
    Write-Host "  .\build-and-run.ps1 -Stop             # Stop the container" -ForegroundColor Gray
    Write-Host "  .\build-and-run.ps1 -Clean            # Clean up everything" -ForegroundColor Gray
}
