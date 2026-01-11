# Azure Storage Troubleshooting Guide

This guide helps diagnose and fix Azure Storage issues in the ImageGenerator function.

## Common Errors

### 1. Access Denied (403 Forbidden)

**Error Message:**
```
Access denied when uploading image to storage. The function may need 'Storage Blob Data Contributor' role.
```

**Cause:**
The Function App's managed identity doesn't have permission to write to the storage account.

**Solution:**

#### Option A: Use Connection String (Simpler)
The function already uses `AzureWebJobsStorage` connection string which should have full access. Verify it's set correctly:

```powershell
az functionapp config appsettings list \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --query "[?name=='AzureWebJobsStorage'].value" -o tsv
```

#### Option B: Grant Managed Identity Access (More Secure)
If using managed identity instead of connection string:

1. **Enable System-Assigned Managed Identity:**
```powershell
az functionapp identity assign \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg
```

2. **Get the Storage Account Name:**
```powershell
$storageAccount = az storage account list \
  --resource-group ai-newspaper-rg \
  --query "[0].name" -o tsv
```

3. **Grant "Storage Blob Data Contributor" Role:**
```powershell
$principalId = az functionapp identity show \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --query principalId -o tsv

az role assignment create \
  --role "Storage Blob Data Contributor" \
  --assignee $principalId \
  --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/ai-newspaper-rg/providers/Microsoft.Storage/storageAccounts/$storageAccount"
```

---

### 2. Container Not Found

**Error Message:**
```
Failed to access blob container 'batch-runs'. Check BLOB_CONTAINER_NAME configuration.
```

**Cause:**
The `BLOB_CONTAINER_NAME` environment variable is not set or the container doesn't exist.

**Solution:**

1. **Set the environment variable:**
```powershell
az functionapp config appsettings set \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --settings "BLOB_CONTAINER_NAME=batch-runs"
```

2. **Create the container manually (if needed):**
```powershell
$storageAccount = az storage account list \
  --resource-group ai-newspaper-rg \
  --query "[0].name" -o tsv

az storage container create \
  --name batch-runs \
  --account-name $storageAccount \
  --public-access blob
```

---

### 3. Invalid Connection String

**Error Message:**
```
Storage connection string has invalid format. Check AzureWebJobsStorage environment variable.
```

**Cause:**
The `AzureWebJobsStorage` connection string is malformed or empty.

**Solution:**

1. **Get the correct connection string:**
```powershell
$storageAccount = az storage account list \
  --resource-group ai-newspaper-rg \
  --query "[0].name" -o tsv

$connectionString = az storage account show-connection-string \
  --name $storageAccount \
  --resource-group ai-newspaper-rg \
  --query connectionString -o tsv
```

2. **Update the function app setting:**
```powershell
az functionapp config appsettings set \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --settings "AzureWebJobsStorage=$connectionString"
```

---

### 4. Blob Too Large (413 Request Entity Too Large)

**Error Message:**
```
Image size (XXXXX bytes) exceeds storage limits.
```

**Cause:**
The generated image is too large (unlikely with DALL-E, but possible).

**Solution:**
This is a hard limit. The image generation uses standard size (1024x1024) which should always be within limits. If this occurs:

1. Check the actual image size in logs
2. Verify DALL-E isn't returning corrupted data
3. Consider compressing the image before upload if needed

---

### 5. Network/Timeout Errors

**Error Message:**
```
Failed to upload image to storage.
```

**Cause:**
Network issues or timeouts when uploading to blob storage.

**Solution:**

1. **Check function timeout settings:**
```powershell
az functionapp config show \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --query "functionAppTimeout"
```

2. **Increase timeout if needed:**
```powershell
az functionapp config set \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --timeout 00:10:00
```

---

## Viewing Detailed Logs

### Stream Live Logs
```powershell
az webapp log tail \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg
```

### View Specific Error Logs
```powershell
# Get logs from Application Insights
az monitor app-insights query \
  --app $(az functionapp show --name ai-newspaper-image-generator --resource-group ai-newspaper-rg --query "appServicePlanId" -o tsv) \
  --analytics-query "traces | where message contains 'UploadToAzureStorageAsync' | order by timestamp desc | take 20"
```

---

## Testing Upload Manually

You can test the storage upload independently:

```powershell
# Test with Azure CLI
$storageAccount = az storage account list \
  --resource-group ai-newspaper-rg \
  --query "[0].name" -o tsv

# Create a test file
echo "test" > test.txt

# Upload it
az storage blob upload \
  --account-name $storageAccount \
  --container-name batch-runs \
  --name test/test.txt \
  --file test.txt

# If this fails, there's a permission or configuration issue
```

---

## Complete Diagnostic Checklist

Run through these checks in order:

1. ✅ **Function app is running:**
   ```powershell
   az functionapp show --name ai-newspaper-image-generator --resource-group ai-newspaper-rg --query "state"
   ```

2. ✅ **BLOB_CONTAINER_NAME is set:**
   ```powershell
   az functionapp config appsettings list --name ai-newspaper-image-generator --resource-group ai-newspaper-rg --query "[?name=='BLOB_CONTAINER_NAME']"
   ```

3. ✅ **AzureWebJobsStorage is set:**
   ```powershell
   az functionapp config appsettings list --name ai-newspaper-image-generator --resource-group ai-newspaper-rg --query "[?name=='AzureWebJobsStorage']"
   ```

4. ✅ **Storage account exists:**
   ```powershell
   az storage account list --resource-group ai-newspaper-rg
   ```

5. ✅ **Container exists:**
   ```powershell
   $storageAccount = az storage account list --resource-group ai-newspaper-rg --query "[0].name" -o tsv
   az storage container list --account-name $storageAccount --query "[?name=='batch-runs']"
   ```

6. ✅ **Function logs show detailed error:**
   ```powershell
   az webapp log tail --name ai-newspaper-image-generator --resource-group ai-newspaper-rg
   ```

---

## What the Enhanced Error Handling Does

The updated `UploadToAzureStorageAsync` method now:

1. **Validates inputs** before attempting upload
2. **Validates connection string** format
3. **Catches specific Azure errors:**
   - 403: Permission denied (needs role assignment)
   - 409: Container already exists (not an error)
   - 413: Blob too large
   - Other status codes with detailed messages

4. **Logs at every step:**
   - Connection string validation
   - Container creation/access
   - Blob path
   - Upload start/completion
   - Detailed error information

5. **Provides actionable error messages** that tell you exactly what to check

---

## Quick Fix Commands

If you just want to make sure everything is configured correctly:

```powershell
# 1. Get storage account
$storageAccount = az storage account list --resource-group ai-newspaper-rg --query "[0].name" -o tsv

# 2. Get connection string
$connectionString = az storage account show-connection-string --name $storageAccount --resource-group ai-newspaper-rg --query connectionString -o tsv

# 3. Create container
az storage container create --name batch-runs --account-name $storageAccount --public-access blob

# 4. Update function app settings
az functionapp config appsettings set \
  --name ai-newspaper-image-generator \
  --resource-group ai-newspaper-rg \
  --settings \
    "AzureWebJobsStorage=$connectionString" \
    "BLOB_CONTAINER_NAME=batch-runs"

# 5. Restart function app
az functionapp restart --name ai-newspaper-image-generator --resource-group ai-newspaper-rg
```

Done! The storage upload should now work.
