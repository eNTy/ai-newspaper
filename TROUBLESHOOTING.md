# Troubleshooting Guide

## GitHub Actions Deployment Issues

### Error: "Invalid client secret provided" (AADSTS7000215)

**Symptom**: GitHub Actions fails during "Login to Azure" step with error:
```
Error: AADSTS7000215: Invalid client secret provided. Ensure the secret being sent in the request is the client secret value, not the client secret ID...
```

**Cause**: The Azure Service Principal credentials in GitHub Secrets have expired or are invalid.

**Solution**: Regenerate the service principal and update the GitHub secret.

#### Steps to Fix:

1. **Run the regeneration script**:

   **Windows (PowerShell)**:
   ```powershell
   cd scripts
   .\regenerate-service-principal.ps1
   ```

   **Linux/Mac (Bash)**:
   ```bash
   cd scripts
   chmod +x regenerate-service-principal.sh
   ./regenerate-service-principal.sh
   ```

2. **Copy the JSON output** from the script (it will look like this):
   ```json
   {
     "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
     "clientSecret": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
     "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
     "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
   }
   ```

3. **Update the GitHub secret**:
   - Go to your repository: `https://github.com/YOUR_USERNAME/ai-newspaper/settings/secrets/actions`
   - Find `AZURE_CREDENTIALS`
   - Click **Update**
   - Paste the JSON from step 2
   - Click **Update secret**

4. **Re-run the failed workflow**:
   - Go to the **Actions** tab
   - Click on the failed workflow run
   - Click **Re-run all jobs**

#### Alternative: Manual Service Principal Creation

If the script doesn't work, you can create the service principal manually:

```bash
# Login to Azure
az login

# Get your subscription ID
az account show --query id -o tsv

# Create service principal (replace {subscription-id} with your actual ID)
az ad sp create-for-rbac \
  --name "ai-newspaper-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/ai-newspaper-rg \
  --sdk-auth
```

Copy the JSON output and update the GitHub secret as described above.

---

## Build Errors

### Package Version Conflicts

**Symptom**: Build fails with package downgrade errors.

**Solution**: Ensure all DurableTask packages use version 1.1.0:
```xml
<PackageReference Include="Microsoft.DurableTask.Client" Version="1.1.0" />
<PackageReference Include="Microsoft.DurableTask.Worker" Version="1.1.0" />
```

### Missing HttpClient Extensions

**Symptom**: `'HttpClient' does not contain a definition for 'PostAsJsonAsync'`

**Solution**: Add the package and using directive:
```xml
<PackageReference Include="System.Net.Http.Json" Version="8.0.0" />
```
```csharp
using System.Net.Http.Json;
```

---

## CORS Issues

**Symptom**: "Cross-origin resource sharing (CORS)" error when testing in Azure Portal.

**Solution**: The workflow automatically adds CORS for `https://portal.azure.com`. If you need to add it manually:

```bash
az functionapp cors add \
  --name ai-newspaper-rss-processor \
  --resource-group ai-newspaper-rg \
  --allowed-origins https://portal.azure.com
```

---

## Local Development Issues

### Azurite Not Running

**Symptom**: `AzureWebJobsStorage` connection fails for durable functions.

**Solution**: Start Azurite:
```bash
azurite
```

Or use Azure Storage Account connection string in `local.settings.json`.

### Function Ports Already in Use

**Symptom**: `Address already in use` or `Port already in use`

**Solution**: Change port in `func start` command:
```bash
func start --port 7075
```

Or kill the process using the port:
```bash
# Windows
netstat -ano | findstr :7071
taskkill /PID <process-id> /F

# Linux/Mac
lsof -ti:7071 | xargs kill -9
```

---

## Claude API Issues

### Insufficient Credits

**Symptom**: `Your credit balance is too low to access the Anthropic API`

**Solution**:
1. Go to https://console.anthropic.com/
2. Add credits to your account
3. Verify API key is correctly configured

### Invalid API Key

**Symptom**: `401 Unauthorized` or `Invalid API Key`

**Solution**:
1. Verify the key in `local.settings.json` starts with `sk-ant-api03-`
2. For Azure, verify the `CLAUDE_API_KEY` app setting:
   ```bash
   az functionapp config appsettings list \
     --name ai-newspaper-rss-processor \
     --resource-group ai-newspaper-rg
   ```

---

## Timer Trigger Not Running

**Symptom**: Daily scheduler doesn't execute at expected time.

**Solution**:
1. Check the CRON expression: `0 0 5 * * *` = 5:00 AM UTC
2. Verify the function is deployed and running
3. Check logs in Azure Portal or via CLI:
   ```bash
   az webapp log tail \
     --name ai-newspaper-orchestrator \
     --resource-group ai-newspaper-rg
   ```

---

## Getting Help

If you encounter issues not covered here:

1. **Check GitHub Actions logs**: Actions tab → Failed workflow → Click on failed step
2. **Check Azure Function logs**: Azure Portal → Function App → Log stream
3. **Check Azure CLI logs**: Run commands with `--debug` flag
4. **Create an issue**: https://github.com/YOUR_USERNAME/ai-newspaper/issues

Include:
- Error message (full text)
- What you were trying to do
- Steps to reproduce
- Environment (Windows/Linux/Mac, .NET version, Azure CLI version)
