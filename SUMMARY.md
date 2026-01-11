# Summary of Changes

## Problem
The NewspaperOrchestrator function was getting **401 Unauthorized** errors when calling RssProcessor, ArticleSimplifier, and ImageGenerator functions because they require function keys for authentication.

## Solution Implemented
Created **individual deployment workflows and scripts** for each Azure Function, with automatic function key configuration.

---

## What Was Created

### 1. GitHub Actions Workflows (Automated Deployment)

Individual workflows for each function:
- [`.github/workflows/deploy-rss-processor.yml`](.github/workflows/deploy-rss-processor.yml)
- [`.github/workflows/deploy-article-simplifier.yml`](.github/workflows/deploy-article-simplifier.yml)
- [`.github/workflows/deploy-image-generator.yml`](.github/workflows/deploy-image-generator.yml)
- [`.github/workflows/deploy-orchestrator.yml`](.github/workflows/deploy-orchestrator.yml)

Each workflow:
- Triggers automatically on push to `master` when files in that function's directory change
- Can be manually triggered from GitHub Actions UI
- Builds, packages, and deploys only that specific function

### 2. PowerShell Deployment Scripts (Manual Deployment)

Individual scripts for local deployment:
- [`scripts/deploy-rss-processor.ps1`](scripts/deploy-rss-processor.ps1)
- [`scripts/deploy-article-simplifier.ps1`](scripts/deploy-article-simplifier.ps1)
- [`scripts/deploy-image-generator.ps1`](scripts/deploy-image-generator.ps1)
- [`scripts/deploy-orchestrator.ps1`](scripts/deploy-orchestrator.ps1)

The **Orchestrator script** automatically:
1. Retrieves function URLs and keys from the three other functions
2. Configures environment variables with full URLs including authentication keys
3. **Fixes the 401 Unauthorized error!**

### 3. Documentation

- [`DEPLOYMENT.md`](DEPLOYMENT.md) - Complete deployment guide
- [`FUNCTION_SETUP.md`](FUNCTION_SETUP.md) - Simple function authentication setup guide

### 4. Updated Main Workflow

- [`.github/workflows/deploy-azure-functions.yml`](.github/workflows/deploy-azure-functions.yml) changed to **manual-only**
  - No longer auto-triggers on push
  - Useful for deploying all functions at once when needed

---

## How to Fix the 401 Error

### Quick Fix (Recommended)
Simply redeploy the Orchestrator:

```powershell
cd scripts
.\deploy-orchestrator.ps1
```

This will automatically:
- Get the latest function keys from the three functions
- Configure the Orchestrator with the correct URLs + keys
- Fix the 401 error!

### Alternative: Manual Configuration

See [`FUNCTION_SETUP.md`](FUNCTION_SETUP.md) for step-by-step manual configuration.

---

## Deployment Workflow

### Individual Function Changes
When you change a single function:

```powershell
# Option 1: Push to GitHub (automatic)
git add .
git commit -m "Update RssProcessor"
git push
# GitHub Actions will automatically deploy RssProcessor

# Option 2: Manual deployment
cd scripts
.\deploy-rss-processor.ps1
```

### Multiple Functions Changed
```powershell
# Deploy each changed function
.\deploy-rss-processor.ps1
.\deploy-article-simplifier.ps1

# Then redeploy orchestrator to refresh keys
.\deploy-orchestrator.ps1
```

### First Time Setup
```powershell
# Deploy supporting functions first (any order)
.\deploy-rss-processor.ps1
.\deploy-article-simplifier.ps1
.\deploy-image-generator.ps1

# Deploy orchestrator last (configures authentication)
.\deploy-orchestrator.ps1
```

---

## Key Benefits

1. **Individual Deployments**: Deploy only what changed, faster CI/CD
2. **Automatic Key Configuration**: No manual key copying needed
3. **Simple and Debuggable**: No complex Key Vault setup, easy to debug locally
4. **Works Everywhere**: Same approach works in Azure and locally
5. **Secure**: Function keys in environment variables, encrypted at rest, transmitted over HTTPS
6. **Retry Logic**: Automatic retry when retrieving function keys, handles timing issues

---

## Files Modified

- `.github/workflows/deploy-azure-functions.yml` - Changed to manual-only
- `lambdas/NewspaperOrchestrator/NewspaperOrchestratorFunction.cs` - Improved error logging
- `lambdas/NewspaperOrchestrator/NewspaperOrchestrator.csproj` - No changes (Key Vault packages already removed)

---

## Next Steps

1. Run `.\deploy-orchestrator.ps1` to fix the 401 error
2. Test the orchestrator by calling StartNewspaperBatch
3. Monitor logs to verify successful function-to-function calls
4. Commit and push the changes to enable automatic deployments

---

## Support

- See [`DEPLOYMENT.md`](DEPLOYMENT.md) for full deployment guide
- See [`FUNCTION_SETUP.md`](FUNCTION_SETUP.md) for authentication setup
- Check function logs: `az webapp log tail --name <function-name> --resource-group ai-newspaper-rg`