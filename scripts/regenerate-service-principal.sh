#!/bin/bash

# Regenerate Service Principal for GitHub Actions
# Run this script when you need to update the AZURE_CREDENTIALS secret

RESOURCE_GROUP="ai-newspaper-rg"

echo "=================================="
echo "Regenerate Service Principal"
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
    az login
    if [ $? -ne 0 ]; then
        echo ""
        echo "Error: Azure login failed."
        read -p "Press Enter to exit..."
        exit 1
    fi
else
    echo "Already logged in to Azure."
fi

echo ""
SELECTED_SUB=$(az account show --query name -o tsv)
echo "Using subscription: $SELECTED_SUB"
echo ""

# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Check if service principal already exists
echo "Checking for existing service principal..."
EXISTING_SP=$(az ad sp list --display-name "ai-newspaper-github-actions" --query "[0].appId" -o tsv 2>/dev/null)

if [ ! -z "$EXISTING_SP" ]; then
    echo "Found existing service principal: $EXISTING_SP"
    echo ""
    read -p "Do you want to delete and recreate it? (y/n): " CONFIRM

    if [ "$CONFIRM" = "y" ] || [ "$CONFIRM" = "Y" ]; then
        echo "Deleting existing service principal..."
        az ad sp delete --id "$EXISTING_SP"

        if [ $? -ne 0 ]; then
            echo "Warning: Failed to delete service principal, but continuing..."
        else
            echo "Deleted successfully."
        fi

        # Wait a bit for deletion to propagate
        echo "Waiting for deletion to propagate..."
        sleep 5
    else
        echo "Cancelled by user."
        read -p "Press Enter to exit..."
        exit 0
    fi
fi

# Create new Service Principal
echo ""
echo "Creating new Service Principal..."
SP_OUTPUT=$(az ad sp create-for-rbac \
    --name "ai-newspaper-github-actions" \
    --role contributor \
    --scopes "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP" \
    --sdk-auth)

if [ $? -ne 0 ]; then
    echo ""
    echo "Error: Failed to create Service Principal"
    echo ""
    echo "Common reasons:"
    echo "1. You don't have permission to create service principals"
    echo "2. The name 'ai-newspaper-github-actions' is still in use"
    echo "3. Resource group doesn't exist"
    echo ""
    echo "Try running this command manually:"
    echo "az ad sp create-for-rbac --name \"ai-newspaper-github-actions-v2\" --role contributor --scopes \"/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP\" --sdk-auth"
    echo ""
    read -p "Press Enter to exit..."
    exit 1
fi

echo ""
echo "=================================="
echo "Service Principal Created!"
echo "=================================="
echo ""
echo "Update your GitHub secret:"
echo ""
echo "1. Go to: https://github.com/YOUR_USERNAME/ai-newspaper/settings/secrets/actions"
echo "2. Find the AZURE_CREDENTIALS secret"
echo "3. Click 'Update' (or 'New repository secret' if it doesn't exist)"
echo "4. Paste the following JSON as the value:"
echo ""
echo "=================================="
echo "$SP_OUTPUT"
echo "=================================="
echo ""
echo "IMPORTANT: Copy the JSON above!"
echo ""
read -p "Press Enter to exit..."
