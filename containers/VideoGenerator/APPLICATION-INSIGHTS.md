# Application Insights Integration

The VideoGenerator Container App is fully integrated with **Azure Application Insights** for comprehensive monitoring and structured logging.

## What's Configured

### Automatic Setup
The setup script (`scripts/setup-video-generator-aca.ps1`) automatically:
1. Creates Application Insights resource (`ai-newspaper-app-insights`)
2. Retrieves the connection string
3. Configures the Container App with the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable

### Code Integration
- **Package**: `Microsoft.ApplicationInsights.AspNetCore` v2.22.0
- **Service**: `builder.Services.AddApplicationInsightsTelemetry()`
- **Configuration**: Automatically picks up connection string from environment variable

## What Gets Tracked

### 1. Request Telemetry
Every HTTP request to `/api/generate` is tracked with:
- **Duration**: How long the request took
- **Status Code**: Success (200) or error codes
- **Properties**: Number of folders processed, success/failure counts
- **URL**: Request path and query parameters

### 2. Trace Logs (ILogger)
All `logger` calls are automatically sent to Application Insights:

```csharp
logger.LogInformation("Processing folder: {StorageFolder}", storageFolder);
logger.LogWarning("Blob not found: {BlobPath}", blobPath);
logger.LogError(ex, "Failed to generate video for folder: {StorageFolder}", storageFolder);
```

These appear in the `traces` table with:
- **Timestamp**: When the log occurred
- **Severity Level**: Information (1), Warning (2), Error (3)
- **Message**: The log message with structured properties
- **Custom Properties**: All parameters (e.g., `StorageFolder`, `BlobPath`)

### 3. Dependency Tracking
External calls are automatically tracked:
- **Azure Storage**: Blob uploads, downloads, existence checks
- **Duration**: How long each dependency call took
- **Success/Failure**: Whether the call succeeded
- **Target**: The storage account/container being accessed

### 4. Exception Tracking
All exceptions are tracked with:
- **Stack Trace**: Full exception details
- **Custom Properties**: Context (folder being processed, file paths, etc.)
- **Correlation**: Linked to the request that caused the exception

### 5. Performance Metrics
- **FFMPEG Processing Time**: Duration of video generation
- **Blob Upload/Download Time**: Storage operation performance
- **Overall Request Duration**: End-to-end processing time

## How to View Logs

### Azure Portal - Application Insights

1. **Navigate to Application Insights**
   - Go to Azure Portal
   - Find resource: `ai-newspaper-app-insights`
   - Click **"Logs"** under Monitoring

2. **Run KQL Queries**

#### All logs from VideoGenerator
```kusto
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| order by timestamp desc
| take 100
```

#### Only errors
```kusto
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| where severityLevel >= 3  // Error level
| order by timestamp desc
```

#### Search for specific folder
```kusto
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| where message contains "age-8/2024-01-13/article-0"
| order by timestamp desc
```

#### Request performance statistics
```kusto
requests
| where cloud_RoleName == "ai-newspaper-video-generator"
| where name == "POST /api/generate"
| summarize
    AvgDuration = avg(duration),
    MaxDuration = max(duration),
    RequestCount = count(),
    SuccessRate = countif(success == true) * 100.0 / count()
| project AvgDuration, MaxDuration, RequestCount, SuccessRate
```

#### Video generation timeline
```kusto
traces
| where cloud_RoleName == "ai-newspaper-video-generator"
| where message contains "video generation" or message contains "FFMPEG"
| order by timestamp desc
| project timestamp, severityLevel, message
```

#### Failed requests with errors
```kusto
requests
| where cloud_RoleName == "ai-newspaper-video-generator"
| where success == false
| join kind=inner (
    exceptions
    | where cloud_RoleName == "ai-newspaper-video-generator"
) on operation_Id
| project timestamp, name, resultCode, problemId, outerMessage
| order by timestamp desc
```

#### Storage dependency performance
```kusto
dependencies
| where cloud_RoleName == "ai-newspaper-video-generator"
| where type == "Azure blob"
| summarize
    AvgDuration = avg(duration),
    MaxDuration = max(duration),
    CallCount = count()
    by name
| order by AvgDuration desc
```

### Azure Portal - Live Metrics

For real-time monitoring:
1. Go to Application Insights → `ai-newspaper-app-insights`
2. Click **"Live Metrics"** under Investigate
3. See live requests, dependencies, and logs as they happen

### Container Logs (Fallback)

If Application Insights is unavailable, use container logs:
```bash
az containerapp logs show \
  --name ai-newspaper-video-generator \
  --resource-group ai-newspaper-rg \
  --tail 100 \
  --follow
```

## Log Correlation

Application Insights automatically correlates all logs from a single request:

1. **Operation ID**: Unique identifier for each HTTP request
2. **Parent ID**: Links child operations to the parent request
3. **Trace Context**: Follows W3C Trace Context standard

Example correlation:
```
Request: POST /api/generate [operation_id: abc123]
  ├─ Trace: Processing folder: age-8/article-0 [operation_id: abc123]
  ├─ Dependency: Download blob: age-8/article-0/image.png [operation_id: abc123]
  ├─ Dependency: Download blob: age-8/article-0/speech.mp3 [operation_id: abc123]
  ├─ Trace: FFMPEG exit code: 0 [operation_id: abc123]
  └─ Dependency: Upload blob: age-8/article-0/video.mp4 [operation_id: abc123]
```

## Alerting (Optional)

You can set up alerts in Application Insights:

### Example: Alert on High Error Rate
```kusto
requests
| where cloud_RoleName == "ai-newspaper-video-generator"
| where success == false
| summarize ErrorRate = count() * 100.0 / toscalar(requests | count())
| where ErrorRate > 10  // Alert if >10% failure rate
```

### Example: Alert on Slow Performance
```kusto
requests
| where cloud_RoleName == "ai-newspaper-video-generator"
| where duration > 300000  // Alert if request takes >5 minutes
```

## Cost

**Application Insights pricing (Pay-as-you-go):**
- First 5 GB/month: Free
- Additional data: ~$2.88 per GB

**Estimated VideoGenerator usage:**
- ~1-2 MB per video generation (logs + telemetry)
- 1,000 videos/month = ~1-2 GB/month
- **Cost**: Free (under 5 GB limit)

For production workloads with >5 GB/month, consider:
- Setting sampling rate: `builder.Services.AddApplicationInsightsTelemetry(options => options.SamplingSettings.SamplingPercentage = 50);`
- Filtering verbose logs
- Using data cap

## Troubleshooting

### Logs not appearing in Application Insights

1. **Check environment variable**
   ```bash
   az containerapp show \
     --name ai-newspaper-video-generator \
     --resource-group ai-newspaper-rg \
     --query "properties.template.containers[0].env" \
     --output table
   ```

   Should see: `APPLICATIONINSIGHTS_CONNECTION_STRING`

2. **Check Application Insights exists**
   ```bash
   az monitor app-insights component show \
     --app ai-newspaper-app-insights \
     --resource-group ai-newspaper-rg
   ```

3. **Restart container app**
   ```bash
   az containerapp revision restart \
     --name ai-newspaper-video-generator \
     --resource-group ai-newspaper-rg
   ```

4. **Wait 2-5 minutes**
   - Telemetry ingestion has a slight delay
   - Check Live Metrics for real-time verification

### Container logs vs Application Insights

| Feature | Container Logs | Application Insights |
|---------|----------------|---------------------|
| Access | CLI only | Azure Portal + CLI + API |
| Retention | 7 days | 90 days (configurable) |
| Search | Limited | Advanced KQL queries |
| Correlation | None | Automatic request correlation |
| Performance | Not tracked | Full telemetry |
| Alerting | No | Yes |
| Cost | Free | Free (<5GB/month) |

**Recommendation**: Use Application Insights for production monitoring, container logs for quick debugging.

## Summary

✅ **Automatic setup** via `setup-video-generator-aca.ps1`
✅ **Comprehensive tracking** - requests, logs, dependencies, exceptions
✅ **Structured logging** with custom properties
✅ **Request correlation** for end-to-end tracing
✅ **Cost-effective** - free for typical usage
✅ **Query flexibility** with KQL
✅ **Real-time monitoring** via Live Metrics

Application Insights provides production-grade observability for the VideoGenerator Container App with zero additional code changes required!
