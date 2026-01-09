# GitHub Secrets Configuration

This document lists all the secrets needed for GitHub Actions CI/CD.

## Required Secrets

Go to: **GitHub Repository** → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**

### 1. AZURE_CREDENTIALS

**Description**: Service Principal credentials for Azure authentication

**How to get**: Run the setup script or create manually:
```bash
az ad sp create-for-rbac \
  --name "ai-newspaper-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/ai-newspaper-rg \
  --sdk-auth
```

**Format**: JSON object
```json
{
  "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientSecret": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

---

### 2. AZURE_FUNCTIONAPP_RSS_PROCESSOR

**Description**: Name of the RSS Processor Function App in Azure

**Value**: `ai-newspaper-rss-processor`

**Note**: If you chose a different name during setup, use that instead

---

### 3. AZURE_FUNCTIONAPP_ARTICLE_SIMPLIFIER

**Description**: Name of the Article Simplifier Function App in Azure

**Value**: `ai-newspaper-article-simplifier`

**Note**: If you chose a different name during setup, use that instead

---

### 4. AZURE_FUNCTIONAPP_IMAGE_GENERATOR

**Description**: Name of the Image Generator Function App in Azure

**Value**: `ai-newspaper-image-generator`

**Note**: If you chose a different name during setup, use that instead

---

### 5. AZURE_FUNCTIONAPP_ORCHESTRATOR

**Description**: Name of the Newspaper Orchestrator Function App in Azure

**Value**: `ai-newspaper-orchestrator`

**Note**: If you chose a different name during setup, use that instead

---

### 6. CLAUDE_API_KEY

**Description**: Your Claude AI API key for accessing Claude Sonnet

**How to get**:
1. Go to https://console.anthropic.com/
2. Create an account or sign in
3. Navigate to API Keys section
4. Create a new API key

**Format**: String starting with `sk-ant-api03-...`

**Example**: `sk-ant-api03-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`

---

## Verification

After adding all secrets, you should have 6 secrets configured:

```
✅ AZURE_CREDENTIALS
✅ AZURE_FUNCTIONAPP_RSS_PROCESSOR
✅ AZURE_FUNCTIONAPP_ARTICLE_SIMPLIFIER
✅ AZURE_FUNCTIONAPP_IMAGE_GENERATOR
✅ AZURE_FUNCTIONAPP_ORCHESTRATOR
✅ CLAUDE_API_KEY
```

## Security Best Practices

1. **Never commit secrets to git** - They are in `.gitignore`
2. **Rotate secrets regularly** - Update every 90 days
3. **Use least privilege** - Service Principal has only Contributor role on the resource group
4. **Monitor usage** - Check GitHub Actions logs for unauthorized access
5. **Revoke old secrets** - When updating, delete old Service Principals

## Updating Secrets

### Update Azure Credentials
```bash
# Delete old service principal
az ad sp delete --id {old-client-id}

# Create new one
az ad sp create-for-rbac \
  --name "ai-newspaper-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/ai-newspaper-rg \
  --sdk-auth
```

### Update Claude API Key
1. Generate new key in Claude console
2. Update GitHub secret
3. Update Azure Function app settings:
```bash
az functionapp config appsettings set \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg \
  --settings "CLAUDE_API_KEY=new-key"

# Repeat for other function apps
```

## Troubleshooting

### Deployment fails with "Invalid credentials"
- Verify `AZURE_CREDENTIALS` is valid JSON
- Check Service Principal hasn't expired
- Ensure Service Principal has correct permissions

### Functions return 401 with Claude API
- Verify `CLAUDE_API_KEY` is set correctly in GitHub secrets
- Check API key is valid in Claude console
- Ensure you have sufficient API credits

### Wrong function app names
- Check function app names in Azure Portal
- Update the three `AZURE_FUNCTIONAPP_*` secrets to match
