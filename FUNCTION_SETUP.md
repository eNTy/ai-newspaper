# Simple Azure Function Authentication Setup

## The Problem

Your NewspaperOrchestrator function is getting a 401 Unauthorized error when calling RssProcessor, ArticleSimplifier, and ImageGenerator functions because they require function keys for authentication.

## The Simple Solution

Include the function keys directly in the environment variable URLs. This works both locally and in Azure with zero additional complexity.

---

## Step 1: Get Function Keys from Azure

For each function app, get the function key:

### Via Azure Portal:
1. Go to your Function App (e.g., "RssProcessor")
2. Navigate to **Functions** → Click on your function name
3. Click **Function Keys** (left menu)
4. Copy the **default** key value

### Via Azure CLI:
```bash
# Get RssProcessor key
az functionapp function keys list \
  --name <your-rssprocessor-app-name> \
  --resource-group <your-resource-group> \
  --function-name RssProcessor \
  --query default -o tsv

# Get ArticleSimplifier key
az functionapp function keys list \
  --name <your-articlesimplifier-app-name> \
  --resource-group <your-resource-group> \
  --function-name ArticleSimplifier \
  --query default -o tsv

# Get ImageGenerator key
az functionapp function keys list \
  --name <your-imagegenerator-app-name> \
  --resource-group <your-resource-group> \
  --function-name ImageGenerator \
  --query default -o tsv
```

---

## Step 2: Configure Environment Variables

### For Azure (Production):

1. Go to your **NewspaperOrchestrator** Function App in Azure Portal
2. Navigate to **Settings** → **Configuration**
3. Under **Application settings**, add/update these variables:

```
RSS_PROCESSOR_URL=https://<your-rssprocessor-app>.azurewebsites.net/api/RssProcessor?code=<PASTE_RSS_KEY_HERE>

ARTICLE_SIMPLIFIER_URL=https://<your-articlesimplifier-app>.azurewebsites.net/api/ArticleSimplifier?code=<PASTE_SIMPLIFIER_KEY_HERE>

IMAGE_GENERATOR_URL=https://<your-imagegenerator-app>.azurewebsites.net/api/ImageGenerator?code=<PASTE_IMAGE_KEY_HERE>
```

4. Click **Save**
5. Restart the function app

### For Local Development:

Edit `lambdas/NewspaperOrchestrator/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",

    "RSS_PROCESSOR_URL": "http://localhost:7071/api/RssProcessor",
    "ARTICLE_SIMPLIFIER_URL": "http://localhost:7072/api/ArticleSimplifier",
    "IMAGE_GENERATOR_URL": "http://localhost:7073/api/ImageGenerator",

    "DEFAULT_RSS_URL": "https://example.com/rss"
  }
}
```

**Note:** For local development, you don't need function keys if all functions are running locally without authentication. If they have `AuthorizationLevel.Function` even locally, add `?code=<key>` to the URLs.

---

## Step 3: Deploy Updated Code

```bash
cd lambdas/NewspaperOrchestrator

# Build
dotnet build

# Deploy to Azure (using your preferred method)
# Option 1: VS Code Azure Functions extension (right-click → Deploy to Function App)
# Option 2: Azure CLI
func azure functionapp publish <your-newspaper-orchestrator-app-name>
```

---

## That's It!

Your NewspaperOrchestrator will now be able to call the other functions successfully. The 401 error will be resolved.

---

## Testing

Test by calling your StartNewspaperBatch endpoint:

```bash
curl -X POST https://<your-orchestrator-app>.azurewebsites.net/api/StartNewspaperBatch?code=<YOUR_ORCHESTRATOR_KEY> \
  -H "Content-Type: application/json" \
  -d '{
    "rssUrl": "https://www.bbc.com/news/rss.xml",
    "audienceAge": 12,
    "storageFolder": "test-batch"
  }'
```

---

## Security Notes

**Is this secure?**
Yes! The function keys are:
- Stored in Azure App Configuration (encrypted at rest)
- Transmitted over HTTPS
- Not exposed in logs
- Can be rotated anytime

**For even better security:**
- Use Azure Key Vault references in App Settings (format: `@Microsoft.KeyVault(SecretUri=...)`)
- Enable Managed Identity for function-to-function auth (more complex, only needed for highly sensitive scenarios)
- Use VNet integration and private endpoints to prevent public internet access

But for most scenarios, function keys in environment variables are perfectly secure and the recommended approach.
