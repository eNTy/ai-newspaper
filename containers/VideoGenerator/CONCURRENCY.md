# VideoGenerator Concurrency Control

## Problem

**FFMPEG is memory and CPU intensive.** With 2GB RAM and 1.0 CPU core, running multiple video generations in parallel would cause:
- Out of Memory (OOM) errors (exit code 137)
- CPU contention and slow processing
- Unpredictable failures

## Solution: Global Semaphore

The VideoGenerator uses a **SemaphoreSlim(1, 1)** to ensure **only one video generation happens at a time per instance**.

### Code Implementation

```csharp
// Global semaphore (at app startup)
var videoGenerationSemaphore = new SemaphoreSlim(1, 1);

// In the request handler
await videoGenerationSemaphore.WaitAsync();  // Wait for slot
try {
    // Generate video (FFMPEG processing)
    await GenerateVideoAsync(...);
}
finally {
    videoGenerationSemaphore.Release();  // Free slot
}
```

## Behavior

### Single Instance

```
Time →
═══════════════════════════════════════════════════════════════

Request A [folder1, folder2]     │ folder1 ▓▓▓▓▓ │ folder2 ▓▓▓▓▓ │
Request B [folder3]                                │ folder3 ▓▓▓▓▓ │
Request C [folder4]                                              │ folder4 ▓▓▓▓▓ │
                                   └── Queued ──┘  └── Queued ──┘
```

- **Request A** arrives first, processes folder1 then folder2
- **Request B** arrives while A is processing, waits in queue
- **Request C** arrives while B is waiting, queues behind B
- **Result**: Sequential processing, no memory contention

### Multiple Instances (Azure Auto-Scaling)

```
Instance 1: Request A [folder1, folder2] → ▓▓▓▓▓▓▓▓▓▓
Instance 2: Request B [folder3]          → ▓▓▓▓▓
Instance 3: Request C [folder4]          → ▓▓▓▓▓
```

- Azure creates new instances when requests queue up
- Each instance has its own semaphore (per-instance limit)
- **Result**: Parallel processing across instances, sequential within instance

## Scaling Configuration

### Current Setup

```bash
--min-replicas 0       # Scale to zero when idle (cost savings)
--max-replicas 10      # Scale up to 10 instances
--cpu 1.0              # 1 CPU core per instance
--memory 2.0Gi         # 2GB RAM per instance
```

### Throughput Capacity

| Scenario | Instances | Concurrent Videos | Notes |
|----------|-----------|-------------------|-------|
| Idle | 0 | 0 | Scales to zero, ~10-30s cold start |
| Low load | 1 | 1 | One video at a time |
| Medium load | 3-5 | 3-5 | Azure auto-scales based on requests |
| High load | 10 | 10 | Maximum throughput |

### Cost Impact

- **Single instance**: ~$5-10/month (scale-to-zero)
- **Always-on (min=1)**: ~$15-20/month (no cold starts)
- **Peak (10 instances)**: ~$50-100/month (only when active)

## Testing Concurrency

Use the provided test script:

```powershell
# Test locally
.\test-concurrent-requests.ps1

# Test in Azure
.\test-concurrent-requests.ps1 -Azure
```

Expected results:
- Requests complete successfully
- Total duration ≈ sum of individual durations (proving sequential processing)
- No OOM errors
- Logs show "Waiting for video generation slot..." messages

## Monitoring

Check logs for concurrency behavior:

```bash
az containerapp logs show \
  --name ai-newspaper-video-generator \
  --resource-group ai-newspaper-rg \
  --tail 100
```

Look for:
- `"Waiting for video generation slot..."` - Request is queued
- `"Starting video generation for folder: X"` - Processing started
- `"Released video generation slot"` - Processing completed

## Alternatives Considered

### ❌ No Concurrency Control
- **Problem**: Multiple FFMPEG processes would run simultaneously
- **Result**: OOM errors (exit code 137)

### ❌ Increase Memory to 4-8GB
- **Problem**: Costs increase 2-4x
- **Result**: Not cost-effective for sporadic workloads

### ❌ Allow 2 Concurrent per Instance
```csharp
var videoGenerationSemaphore = new SemaphoreSlim(2, 2);
```
- **Problem**: Requires 4GB+ RAM per instance
- **Result**: Higher costs, marginal throughput gain

### ✅ Current Solution: Semaphore + Auto-Scaling
- **Benefits**:
  - Prevents OOM errors
  - Cost-effective (scale-to-zero)
  - Scales horizontally for high load
  - Simple implementation

## Performance Tuning

If you need higher throughput:

1. **Let Azure scale automatically** (recommended)
   - No code changes needed
   - Handles bursts gracefully
   - Cost-effective

2. **Increase max replicas**
   ```bash
   az containerapp update --max-replicas 20
   ```

3. **Set min replicas > 0** (avoid cold starts)
   ```bash
   az containerapp update --min-replicas 1
   ```

4. **Use faster preset** (if quality loss acceptable)
   ```csharp
   // Change in Program.cs
   -preset medium  →  -preset faster
   ```

## Summary

- ✅ **Prevents OOM errors** with semaphore concurrency control
- ✅ **Cost-effective** with scale-to-zero capability
- ✅ **Scales horizontally** up to 10 videos in parallel (10 instances)
- ✅ **Simple implementation** with SemaphoreSlim
- ✅ **Predictable behavior** - one video per instance at a time
