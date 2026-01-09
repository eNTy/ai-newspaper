#!/bin/bash

# Azure Resources Setup Script for AI Newspaper
# This script creates all necessary Azure resources for the project

# Configuration
RESOURCE_GROUP="ai-newspaper-rg"
LOCATION="westeurope"
STORAGE_ACCOUNT="ainewspaperstorage$(date +%s)"
APP_SERVICE_PLAN="ai-newspaper-plan"

# Function App Names
RSS_PROCESSOR_APP="ai-newspaper-rss-processor"
ARTICLE_SIMPLIFIER_APP="ai-newspaper-article-simplifier"
IMAGE_GENERATOR_APP="ai-newspaper-image-generator"

# Error handling function
check_error() {
    if [ $? -ne 0 ]; then
        echo ""
        echo "Error: $1"
        echo "Press Enter to exit..."
        read
        exit 1
    fi
}

echo "=================================="
echo "AI Newspaper - Azure Setup"
echo "=================================="
echo ""

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo "Error: Azure CLI is not installed. Please install it first."
    echo "Visit: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    read -p "Press Enter to exit..."
    exit 1
fi

# Check if already logged in
echo "Checking Azure login status..."
if ! az account show &> /dev/null; then
    echo "Not logged in to Azure. Opening browser for login..."
    echo ""
    if ! az login; then
        echo ""
        echo "Error: Azure login failed."
        read -p "Press Enter to exit..."
        exit 1
    fi
else
    echo "Already logged in to Azure."
fi

# Select subscription (if multiple)
echo ""
echo "Selecting Azure subscription..."
az account list --output table
echo ""
read -p "Enter subscription ID (or press Enter for default): " SUBSCRIPTION_ID
if [ ! -z "$SUBSCRIPTION_ID" ]; then
    az account set --subscription "$SUBSCRIPTION_ID"
fi

SELECTED_SUB=$(az account show --query name -o tsv)
echo "Using subscription: $SELECTED_SUB"
echo ""

# Create Resource Group
echo "Creating resource group: $RESOURCE_GROUP..."
az group create \
    --name "$RESOURCE_GROUP" \
    --location "$LOCATION"
check_error "Failed to create resource group"

# Create Storage Account
echo ""
echo "Creating storage account: $STORAGE_ACCOUNT..."
az storage account create \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --sku Standard_LRS \
    --kind StorageV2

# Get Storage Connection String
STORAGE_CONNECTION=$(az storage account show-connection-string \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --query connectionString \
    --output tsv)

# Create App Service Plan (Consumption/Serverless)
echo ""
echo "Creating App Service Plan: $APP_SERVICE_PLAN..."
az functionapp plan create \
    --name "$APP_SERVICE_PLAN" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --sku Y1 \
    --is-linux

# Create Function Apps
echo ""
echo "Creating Function App: $RSS_PROCESSOR_APP..."
az functionapp create \
    --name "$RSS_PROCESSOR_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --plan "$APP_SERVICE_PLAN" \
    --storage-account "$STORAGE_ACCOUNT" \
    --runtime dotnet-isolated \
    --runtime-version 8 \
    --functions-version 4 \
    --os-type Linux

echo ""
echo "Creating Function App: $ARTICLE_SIMPLIFIER_APP..."
az functionapp create \
    --name "$ARTICLE_SIMPLIFIER_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --plan "$APP_SERVICE_PLAN" \
    --storage-account "$STORAGE_ACCOUNT" \
    --runtime dotnet-isolated \
    --runtime-version 8 \
    --functions-version 4 \
    --os-type Linux

echo ""
echo "Creating Function App: $IMAGE_GENERATOR_APP..."
az functionapp create \
    --name "$IMAGE_GENERATOR_APP" \
    --resource-group "$RESOURCE_GROUP" \
    --plan "$APP_SERVICE_PLAN" \
    --storage-account "$STORAGE_ACCOUNT" \
    --runtime dotnet-isolated \
    --runtime-version 8 \
    --functions-version 4 \
    --os-type Linux

# Create Service Principal for GitHub Actions
echo ""
echo "Creating Service Principal for GitHub Actions..."
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

SP_OUTPUT=$(az ad sp create-for-rbac \
    --name "ai-newspaper-github-actions" \
    --role contributor \
    --scopes "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP" \
    --sdk-auth)

echo ""
echo "=================================="
echo "Setup Complete!"
echo "=================================="
echo ""
echo "Resource Group: $RESOURCE_GROUP"
echo "Storage Account: $STORAGE_ACCOUNT"
echo "Function Apps:"
echo "  - $RSS_PROCESSOR_APP"
echo "  - $ARTICLE_SIMPLIFIER_APP"
echo "  - $IMAGE_GENERATOR_APP"
echo ""
echo "=================================="
echo "GitHub Secrets Configuration"
echo "=================================="
echo ""
echo "Add the following secrets to your GitHub repository:"
echo "(Settings > Secrets and variables > Actions > New repository secret)"
echo ""
echo "1. AZURE_CREDENTIALS"
echo "   Value:"
echo "$SP_OUTPUT"
echo ""
echo "2. AZURE_FUNCTIONAPP_RSS_PROCESSOR"
echo "   Value: $RSS_PROCESSOR_APP"
echo ""
echo "3. AZURE_FUNCTIONAPP_ARTICLE_SIMPLIFIER"
echo "   Value: $ARTICLE_SIMPLIFIER_APP"
echo ""
echo "4. AZURE_FUNCTIONAPP_IMAGE_GENERATOR"
echo "   Value: $IMAGE_GENERATOR_APP"
echo ""
echo "5. CLAUDE_API_KEY"
echo "   Value: <your-claude-api-key>"
echo ""
echo "=================================="
echo "Next Steps"
echo "=================================="
echo "1. Add the above secrets to GitHub"
echo "2. Push code to trigger deployment"
echo "3. Monitor deployment in GitHub Actions tab"
echo ""
echo "Press Enter to exit..."
read
