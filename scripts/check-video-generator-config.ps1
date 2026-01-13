# Check VideoGenerator Container App Configuration
# Shows environment variables, resource allocation, and status

param(
    [string]$ResourceGroup = "ai-newspaper-rg",
    [string]$ContainerAppName = "ai-newspaper-video-generator"
)

Write-Host "=== VideoGenerator Container App Configuration ===" -ForegroundColor Cyan
Write-Host ""

# Check if Container App exists
Write-Host "Checking if Container App exists..." -ForegroundColor Yellow
$appExists = az containerapp show --name $ContainerAppName --resource-group $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Container App not found!" -ForegroundColor Red
    Write-Host "Resource Group: $ResourceGroup" -ForegroundColor Red
    Write-Host "Container App: $ContainerAppName" -ForegroundColor Red
    exit 1
}

Write-Host "Container App found." -ForegroundColor Green
Write-Host ""

# Get environment variables
Write-Host "=== Environment Variables ===" -ForegroundColor Cyan
$envVars = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.template.containers[0].env" `
    --output json | ConvertFrom-Json

if ($envVars) {
    foreach ($env in $envVars) {
        $name = $env.name
        $value = $env.value

        # Mask sensitive values
        if ($name -like "*CONNECTION*" -or $name -like "*KEY*" -or $name -like "*SECRET*") {
            $maskedValue = $value.Substring(0, [Math]::Min(20, $value.Length)) + "***"
            Write-Host "  $name = $maskedValue" -ForegroundColor Gray
        } else {
            Write-Host "  $name = $value" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "  No environment variables found." -ForegroundColor Yellow
}

Write-Host ""

# Get resource allocation
Write-Host "=== Resource Allocation ===" -ForegroundColor Cyan
$resources = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.template.containers[0].resources" `
    --output json | ConvertFrom-Json

if ($resources) {
    Write-Host "  CPU: $($resources.cpu)" -ForegroundColor Gray
    Write-Host "  Memory: $($resources.memory)" -ForegroundColor Gray
} else {
    Write-Host "  Could not retrieve resource information." -ForegroundColor Yellow
}

Write-Host ""

# Get scaling configuration
Write-Host "=== Scaling Configuration ===" -ForegroundColor Cyan
$scale = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.template.scale" `
    --output json | ConvertFrom-Json

if ($scale) {
    Write-Host "  Min Replicas: $($scale.minReplicas)" -ForegroundColor Gray
    Write-Host "  Max Replicas: $($scale.maxReplicas)" -ForegroundColor Gray
} else {
    Write-Host "  Could not retrieve scaling information." -ForegroundColor Yellow
}

Write-Host ""

# Get ingress configuration
Write-Host "=== Ingress Configuration ===" -ForegroundColor Cyan
$fqdn = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.configuration.ingress.fqdn" `
    --output tsv

$targetPort = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.configuration.ingress.targetPort" `
    --output tsv

$external = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.configuration.ingress.external" `
    --output tsv

if ($fqdn) {
    Write-Host "  URL: https://$fqdn" -ForegroundColor Green
    Write-Host "  Target Port: $targetPort" -ForegroundColor Gray
    Write-Host "  External: $external" -ForegroundColor Gray
} else {
    Write-Host "  No ingress configured." -ForegroundColor Yellow
}

Write-Host ""

# Get container image
Write-Host "=== Container Image ===" -ForegroundColor Cyan
$image = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "properties.template.containers[0].image" `
    --output tsv

Write-Host "  Image: $image" -ForegroundColor Gray

Write-Host ""

# Get revision status
Write-Host "=== Active Revision ===" -ForegroundColor Cyan
$revision = az containerapp revision list `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "[?properties.active==\`true\`].{Name:name,CreatedTime:properties.createdTime,Replicas:properties.replicas}" `
    --output json | ConvertFrom-Json

if ($revision) {
    foreach ($rev in $revision) {
        Write-Host "  Name: $($rev.Name)" -ForegroundColor Gray
        Write-Host "  Created: $($rev.CreatedTime)" -ForegroundColor Gray
        Write-Host "  Replicas: $($rev.Replicas)" -ForegroundColor Gray
    }
} else {
    Write-Host "  No active revision found." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Configuration Check Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "To update environment variables:" -ForegroundColor Yellow
Write-Host "  az containerapp update --name $ContainerAppName --resource-group $ResourceGroup --set-env-vars KEY=VALUE" -ForegroundColor Gray
