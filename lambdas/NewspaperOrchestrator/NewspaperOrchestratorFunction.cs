using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Communication.Email;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace NewspaperOrchestrator;

public class NewspaperOrchestratorFunction
{
    private readonly ILogger<NewspaperOrchestratorFunction> _logger;
    private readonly string _videoGeneratorUrl;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerConfig _containerConfig;
    private readonly EmailClient _emailClient;
    private readonly string _notificationRecipient;
    private readonly string _notificationSender;

    public NewspaperOrchestratorFunction(
        ILogger<NewspaperOrchestratorFunction> logger,
        BlobServiceClient blobServiceClient,
        BlobContainerConfig containerConfig,
        EmailClient emailClient)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
        _containerConfig = containerConfig;
        _emailClient = emailClient;

        _videoGeneratorUrl = Environment.GetEnvironmentVariable("VIDEO_GENERATOR_URL")
            ?? throw new InvalidOperationException("VIDEO_GENERATOR_URL environment variable is not set");
        _notificationRecipient = Environment.GetEnvironmentVariable("NOTIFICATION_EMAIL_TO")
            ?? throw new InvalidOperationException("NOTIFICATION_EMAIL_TO environment variable is not set");
        _notificationSender = Environment.GetEnvironmentVariable("NOTIFICATION_EMAIL_FROM")
            ?? throw new InvalidOperationException("NOTIFICATION_EMAIL_FROM environment variable is not set");

        _logger.LogInformation("NewspaperOrchestratorFunction initialized");
    }

    // HTTP Trigger to start the orchestration
    [Function("StartNewspaperBatch")]
    public async Task<HttpResponseData> StartBatch(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("Starting newspaper batch orchestration");

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<OrchestratorRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request == null || string.IsNullOrEmpty(request.RssUrl))
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteAsJsonAsync(new { error = "Invalid request. RssUrl and AudienceAge are required." });
            return badResponse;
        }

        // Start the orchestration
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(NewspaperBatchOrchestrator),
            request);

        _logger.LogInformation("Started orchestration with ID = {instanceId}", instanceId);

        // Return the management URLs
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new OrchestratorResponse
        {
            InstanceId = instanceId,
            StatusQueryUrl = $"{req.Url.Scheme}://{req.Url.Authority}/runtime/webhooks/durabletask/instances/{instanceId}"
        });

        return response;
    }

    // Orchestrator function
    [Function(nameof(NewspaperBatchOrchestrator))]
    public async Task<BatchResult> NewspaperBatchOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var request = context.GetInput<OrchestratorRequest>()!;
        var logger = context.CreateReplaySafeLogger<NewspaperOrchestratorFunction>();

        logger.LogInformation("Processing RSS feed: {rssUrl} for age: {age}", request.RssUrl, request.AudienceAge);

        var batchStartTime = context.CurrentUtcDateTime;

        var batchResult = new BatchResult
        {
            RssUrl = request.RssUrl,
            AudienceAge = request.AudienceAge,
            Success = false,
            BatchStartTime = batchStartTime,
            StorageFolder = request.StorageFolder
        };

        try
        {
            // Step 0: Warm up the Video Generator container app (async, don't wait)
            logger.LogInformation("Starting Video Generator container warmup");
            var warmupTask = context.CallActivityAsync<bool>(nameof(WarmupVideoGenerator));

            // Step 1: Fetch top articles from RSS
            await FetchTopArticlesStep(context, batchResult, logger);

            // Step 2: Simplify articles in parallel
            await SimplifyArticlesStep(context, batchResult, logger);

            // Step 3: Generate images and audio in parallel
            await GenerateMediaStep(context, batchResult, logger);

            // Step 4: Save article JSONs
            await SaveArticleJsonsStep(context, batchResult, logger);

            // Step 5: Generate videos (async pattern with polling)
            await GenerateVideosStep(context, batchResult, warmupTask, logger);

            // Step 6: Publish videos to Instagram as a carousel
            await PublishToInstagramStep(context, batchResult, logger);

            // Step 7: Persist batch result to storage
            batchResult.Success = true;
            batchResult.BatchEndTime = context.CurrentUtcDateTime;
            batchResult.BatchDuration = batchResult.BatchEndTime - batchResult.BatchStartTime;
            logger.LogInformation("Orchestration completed successfully in {duration}", batchResult.BatchDuration);
            await PersistBatchResultStep(context, batchResult, request.StorageFolder, logger);

            return batchResult;
        }
        catch (OrchestrationStepException ex)
        {
            batchResult.FailedStep = ex.StepName;
            batchResult.ErrorMessage = ex.Message;
            batchResult.BatchEndTime = context.CurrentUtcDateTime;
            batchResult.BatchDuration = batchResult.BatchEndTime - batchResult.BatchStartTime;
            logger.LogError(ex, "Orchestration failed at step: {step} after {duration}", ex.StepName, batchResult.BatchDuration);

            try
            {
                await PersistBatchResultStep(context, batchResult, request.StorageFolder, logger);
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx, "Failed to persist batch result after orchestration failure");
            }

            try
            {
                await context.CallActivityAsync(nameof(SendFailureEmail), batchResult);
            }
            catch (Exception emailEx)
            {
                logger.LogError(emailEx, "Failed to send failure notification email");
            }

            return batchResult;
        }
        catch (Exception ex)
        {
            batchResult.FailedStep = "UnexpectedError";
            batchResult.ErrorMessage = ex.Message;
            batchResult.BatchEndTime = context.CurrentUtcDateTime;
            batchResult.BatchDuration = batchResult.BatchEndTime - batchResult.BatchStartTime;
            logger.LogError(ex, "Orchestration failed with unexpected error after {duration}", batchResult.BatchDuration);

            try
            {
                await PersistBatchResultStep(context, batchResult, request.StorageFolder, logger);
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx, "Failed to persist batch result after orchestration failure");
            }

            try
            {
                await context.CallActivityAsync(nameof(SendFailureEmail), batchResult);
            }
            catch (Exception emailEx)
            {
                logger.LogError(emailEx, "Failed to send failure notification email");
            }

            return batchResult;
        }
    }

    // --- Orchestration step helpers ---

    private async Task FetchTopArticlesStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        ILogger logger)
    {
        try
        {
            var articles = await context.CallActivityAsync<List<ProcessedArticle>>(
                "FetchTopArticles",
                batchResult);

            batchResult.Articles = articles;
            logger.LogInformation("Found {count} articles to process", articles.Count);
        }
        catch (Exception ex)
        {
            throw new OrchestrationStepException("FetchTopArticles", ex.Message, ex);
        }
    }

    private async Task SimplifyArticlesStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        ILogger logger)
    {
        try
        {
            var simplifyTasks = new List<Task<ProcessedArticle>>();
            foreach (var article in batchResult.Articles)
            {
                var input = new ArticleActivityInput
                {
                    Article = article,
                    AudienceAge = batchResult.AudienceAge
                };

                simplifyTasks.Add(context.CallActivityAsync<ProcessedArticle>("SimplifyArticle", input));
            }

            var results = await Task.WhenAll(simplifyTasks);
            logger.LogInformation("Simplified {count} articles", results.Length);

            for (int i = 0; i < results.Length; i++)
            {
                batchResult.Articles[i] = results[i];
            }
        }
        catch (Exception ex)
        {
            throw new OrchestrationStepException("SimplifyArticles", ex.Message, ex);
        }
    }

    private async Task GenerateMediaStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        ILogger logger)
    {
        try
        {
            var imageTasks = new List<Task<ProcessedArticle>>();
            var audioTasks = new List<Task<ProcessedArticle>>();

            for (int i = 0; i < batchResult.Articles.Count; i++)
            {
                var input = new ArticleActivityInput
                {
                    Article = batchResult.Articles[i],
                    AudienceAge = batchResult.AudienceAge
                };

                imageTasks.Add(context.CallActivityAsync<ProcessedArticle>("GenerateImage", input));
                audioTasks.Add(context.CallActivityAsync<ProcessedArticle>("GenerateAudio", input));
            }

            var images = await Task.WhenAll(imageTasks);
            var audios = await Task.WhenAll(audioTasks);
            logger.LogInformation("Generated {imageCount} images and {audioCount} audio files", images.Length, audios.Length);

            for (int i = 0; i < batchResult.Articles.Count; i++)
            {
                batchResult.Articles[i].ImageUrl = images[i].ImageUrl;
                batchResult.Articles[i].ImageDescription = images[i].ImageDescription;
                batchResult.Articles[i].AudioUrl = audios[i].AudioUrl;
            }
        }
        catch (Exception ex)
        {
            throw new OrchestrationStepException("GenerateMedia", ex.Message, ex);
        }
    }

    private async Task SaveArticleJsonsStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        ILogger logger)
    {
        try
        {
            var saveTasks = batchResult.Articles
                .Select(article => context.CallActivityAsync<string>(nameof(SaveArticleJson), article))
                .ToList();

            await Task.WhenAll(saveTasks);
            logger.LogInformation("Saved {count} article JSON files", batchResult.Articles.Count);
        }
        catch (Exception ex)
        {
            throw new OrchestrationStepException("SaveArticleJsons", ex.Message, ex);
        }
    }

    private async Task GenerateVideosStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        Task<bool> warmupTask,
        ILogger logger)
    {
        try
        {
            // Ensure warmup completed
            bool warmupSuccessful = false;
            try
            {
                warmupSuccessful = await warmupTask;
                logger.LogInformation("Container warmup completed (success: {success})", warmupSuccessful);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Container warmup task failed (non-critical)");
            }

            if (!warmupSuccessful)
            {
                throw new InvalidOperationException("Video generation failed: warmup unsuccessful or URL not configured");
            }

            var videoRequest = new VideoGeneratorRequest
            {
                StorageFolders = batchResult.Articles.Select(a => a.StorageFolder).ToArray()
            };

            // Trigger async video generation job
            var jobId = await context.CallActivityAsync<string>(
                nameof(GenerateVideos),
                videoRequest);

            logger.LogInformation("Video generation job started: {jobId}", jobId);

            // Poll for completion with exponential backoff
            var maxAttempts = 60;
            var attempt = 0;
            VideoGenerationStatusResponse? statusResponse = null;

            while (attempt < maxAttempts)
            {
                var delaySeconds = Math.Min(5 + (attempt * 5), 30);
                await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(delaySeconds), CancellationToken.None);

                var checkStatusRequest = new CheckVideoStatusRequest { JobId = jobId };
                statusResponse = await context.CallActivityAsync<VideoGenerationStatusResponse>(
                    nameof(CheckVideoGenerationStatus),
                    checkStatusRequest);

                logger.LogInformation("Poll attempt {attempt}: Job {jobId} status is {status} ({processed}/{total})",
                    attempt + 1, jobId, statusResponse.Status, statusResponse.ProcessedFolders, statusResponse.TotalFolders);

                if (statusResponse.Status == "Completed" || statusResponse.Status == "Failed")
                {
                    break;
                }

                attempt++;
            }

            if (statusResponse == null)
            {
                throw new InvalidOperationException("Video generation status unknown - no response received");
            }
            else if (statusResponse.Status == "Completed")
            {
                var successCount = statusResponse.Results?.Count(r => r.Success) ?? 0;
                var totalCount = statusResponse.TotalFolders;
                var result = $"Generated {successCount}/{totalCount} videos successfully";
                logger.LogInformation(result);

                if (statusResponse.Results != null)
                {
                    foreach (var videoResult in statusResponse.Results.Where(r => r.Success && !string.IsNullOrEmpty(r.VideoUrl)))
                    {
                        var article = batchResult.Articles.FirstOrDefault(a => a.StorageFolder == videoResult.Folder);
                        if (article != null)
                        {
                            article.VideoUrl = videoResult.VideoUrl!;
                        }
                    }
                }

                var failures = statusResponse.Results?.Where(r => !r.Success).ToList() ?? new List<VideoResultItem>();
                foreach (var failure in failures)
                {
                    logger.LogWarning("Video generation failed for folder {folder}: {error}", failure.Folder, failure.Error);
                }

                if (successCount == 0 && totalCount > 0)
                {
                    throw new InvalidOperationException($"All {totalCount} video generations failed");
                }

                batchResult.Result = result;
            }
            else if (statusResponse.Status == "Failed")
            {
                throw new InvalidOperationException($"Video generation failed: {statusResponse.ErrorMessage}");
            }
            else if (attempt >= maxAttempts)
            {
                throw new TimeoutException($"Video generation timed out after {maxAttempts} attempts (status: {statusResponse.Status})");
            }
            else
            {
                throw new InvalidOperationException("Video generation ended with unknown status");
            }
        }
        catch (Exception ex)
        {
            throw new OrchestrationStepException("GenerateVideos", ex.Message, ex);
        }
    }

    private async Task PublishToInstagramStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        ILogger logger)
    {
        try
        {
            var videosAvailable = batchResult.Articles.Any(a => !string.IsNullOrEmpty(a.VideoUrl));
            if (!videosAvailable)
            {
                logger.LogWarning("Skipping Instagram publishing: no videos available");
                return;
            }

            logger.LogInformation("Publishing videos to Instagram");

            var publishResult = await context.CallActivityAsync<InstagramPublishResult>("PublishToInstagram", batchResult);
            batchResult.InstagramMediaId = publishResult.MediaId;
            batchResult.InstagramUrl = publishResult.Permalink;
            logger.LogInformation("Instagram carousel published successfully. Media ID: {mediaId}, URL: {url}",
                publishResult.MediaId, publishResult.Permalink);
        }
        catch (Exception ex)
        {
            throw new OrchestrationStepException("PublishToInstagram", ex.Message, ex);
        }
    }

    private async Task PersistBatchResultStep(
        TaskOrchestrationContext context,
        BatchResult batchResult,
        string storageFolder,
        ILogger logger)
    {
        logger.LogInformation("Persisting batch result to storage");

        await context.CallActivityAsync(nameof(SaveBatchResult), batchResult);

        logger.LogInformation("Batch result persisted successfully");
    }

    // --- Activity: Video Generator (still HTTP to Container App) ---

    [Function(nameof(WarmupVideoGenerator))]
    public async Task<bool> WarmupVideoGenerator(
        [ActivityTrigger] FunctionContext context)
    {
        var logger = context.GetLogger(nameof(WarmupVideoGenerator));

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        try
        {
            httpClient.Timeout = TimeSpan.FromMinutes(2);

            var healthUrl = _videoGeneratorUrl.TrimEnd('/') + "/health";
            logger.LogInformation("Warming up Video Generator container at: {url}", healthUrl);

            var response = await httpClient.GetAsync(healthUrl);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Video Generator container warmup successful");
                return true;
            }
            else
            {
                logger.LogWarning("Video Generator container warmup returned status: {status}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Video Generator container warmup failed (non-critical): {message}", ex.Message);
            return false;
        }
    }

    [Function(nameof(GenerateVideos))]
    public async Task<string> GenerateVideos(
        [ActivityTrigger] VideoGeneratorRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(GenerateVideos));
        logger.LogInformation("Triggering async video generation for {count} articles", request.StorageFolders?.Length ?? 0);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var generateUrl = _videoGeneratorUrl.TrimEnd('/') + "/api/generate";
            logger.LogInformation("Sending async video generation request POST {url}", generateUrl);

            var response = await httpClient.PostAsync(generateUrl, content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<VideoGenerationJobResponse>();

            if (result == null || string.IsNullOrEmpty(result.JobId))
            {
                throw new InvalidOperationException("Failed to get job ID from video generation service");
            }

            logger.LogInformation("Video generation job created: {jobId}", result.JobId);
            return result.JobId;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to trigger video generation. Status: {status}", ex.StatusCode);
            throw;
        }
    }

    [Function(nameof(CheckVideoGenerationStatus))]
    public async Task<VideoGenerationStatusResponse> CheckVideoGenerationStatus(
        [ActivityTrigger] CheckVideoStatusRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(CheckVideoGenerationStatus));
        logger.LogInformation("Checking video generation status for job: {jobId}", request.JobId);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        try
        {
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var statusUrl = _videoGeneratorUrl.TrimEnd('/') + $"/api/generate/status/{request.JobId}";
            logger.LogInformation("Querying status GET {url}", statusUrl);

            var response = await httpClient.GetAsync(statusUrl);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<VideoGenerationStatusResponse>();

            if (result == null)
            {
                throw new InvalidOperationException($"Failed to get status for job {request.JobId}");
            }

            logger.LogInformation("Job {jobId} status: {status}, Processed: {processed}/{total}",
                request.JobId, result.Status, result.ProcessedFolders, result.TotalFolders);

            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to check video generation status. Status: {status}", ex.StatusCode);
            throw;
        }
    }

    // --- Activity: Storage operations ---

    [Function(nameof(SaveArticleJson))]
    public async Task<string> SaveArticleJson(
        [ActivityTrigger] ProcessedArticle article,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SaveArticleJson));
        logger.LogInformation("Saving article JSON for: {title}", article.Title);

        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerConfig.ContainerName);

        var blobPath = $"{article.StorageFolder}/article.json";
        var blobClient = containerClient.GetBlobClient(blobPath);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var jsonContent = JsonSerializer.Serialize(article, jsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(jsonContent);

        using var stream = new MemoryStream(jsonBytes);

        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/json"
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);

        var blobUrl = blobClient.Uri.ToString();
        logger.LogInformation("Saved article JSON to: {url}", blobUrl);

        return blobUrl;
    }

    [Function(nameof(SaveBatchResult))]
    public async Task<string> SaveBatchResult(
        [ActivityTrigger] BatchResult batchResult,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SaveBatchResult));
        logger.LogInformation("Saving batch result (Success: {success}, FailedStep: {failedStep})",
            batchResult.Success, batchResult.FailedStep);

        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerConfig.ContainerName);

        var blobPath = $"{batchResult.StorageFolder}/batch-result.json";
        var blobClient = containerClient.GetBlobClient(blobPath);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var jsonContent = JsonSerializer.Serialize(batchResult, jsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(jsonContent);

        using var stream = new MemoryStream(jsonBytes);

        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/json"
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);

        var blobUrl = blobClient.Uri.ToString();
        logger.LogInformation("Saved batch result to: {url}", blobUrl);

        return blobUrl;
    }

    // --- Activity: Failure notification ---

    [Function(nameof(SendFailureEmail))]
    public async Task SendFailureEmail(
        [ActivityTrigger] BatchResult batchResult,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SendFailureEmail));
        logger.LogInformation("Sending failure notification email for step: {step}", batchResult.FailedStep);

        var subject = $"Newspaper Orchestration Failed: {batchResult.FailedStep}";
        var body = $"""
            <h2>Orchestration Failed</h2>
            <p><strong>Failed Step:</strong> {batchResult.FailedStep}</p>
            <p><strong>Error:</strong> {batchResult.ErrorMessage}</p>
            <p><strong>RSS URL:</strong> {batchResult.RssUrl}</p>
            <p><strong>Audience Age:</strong> {batchResult.AudienceAge}</p>
            <p><strong>Duration:</strong> {batchResult.BatchDuration}</p>
            <p><strong>Storage Folder:</strong> {batchResult.StorageFolder}</p>
            """;

        var emailMessage = new EmailMessage(
            senderAddress: _notificationSender,
            recipientAddress: _notificationRecipient,
            content: new EmailContent(subject) { Html = body });

        await _emailClient.SendAsync(Azure.WaitUntil.Started, emailMessage);
        logger.LogInformation("Failure notification email sent");
    }

    // --- HTTP endpoint for testing email ---

    [Function("TestEmail")]
    public async Task<HttpResponseData> TestEmail(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();

        var emailMessage = new EmailMessage(
            senderAddress: _notificationSender,
            recipientAddress: _notificationRecipient,
            content: new EmailContent("AI Newspaper - Test Email") { Html = body });

        await _emailClient.SendAsync(Azure.WaitUntil.Started, emailMessage);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("Email sent");
        return response;
    }

    // --- HTTP endpoint for status ---

    [Function("GetBatchStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "status/{instanceId}")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId)
    {
        var metadata = await client.GetInstanceAsync(instanceId);

        if (metadata == null)
        {
            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
            await notFoundResponse.WriteStringAsync("Instance not found");
            return notFoundResponse;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            instanceId = metadata.InstanceId,
            runtimeStatus = metadata.RuntimeStatus.ToString(),
            createdAt = metadata.CreatedAt,
            lastUpdatedAt = metadata.LastUpdatedAt,
            output = metadata.ReadOutputAs<BatchResult>()
        });

        return response;
    }

    // --- Timer triggers ---

    // Age 12: 12pm Prague (11:00 UTC) on weekdays
    [Function("DailyNewspaperScheduler_Age12_Weekdays")]
    public async Task RunDailyScheduler_Age12_Weekdays(
        [TimerTrigger("0 0 11 * * 1-5")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(12, client, context);
    }

    // Age 12: 1pm Prague (12:00 UTC) on Saturday
    [Function("DailyNewspaperScheduler_Age12_Saturday")]
    public async Task RunDailyScheduler_Age12_Saturday(
        [TimerTrigger("0 0 12 * * 6")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(12, client, context);
    }

    // Age 12: 8pm Prague (19:00 UTC) on Sunday
    [Function("DailyNewspaperScheduler_Age12_Sunday")]
    public async Task RunDailyScheduler_Age12_Sunday(
        [TimerTrigger("0 0 19 * * 0")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(12, client, context);
    }

    // Age 16: 8pm Prague (19:00 UTC) on weekdays
    [Function("DailyNewspaperScheduler_Age16_Weekdays")]
    public async Task RunDailyScheduler_Age16_Weekdays(
        [TimerTrigger("0 0 19 * * 1-5")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(16, client, context);
    }

    // Age 16: 9pm Prague (20:00 UTC) on weekends
    [Function("DailyNewspaperScheduler_Age16_Weekends")]
    public async Task RunDailyScheduler_Age16_Weekends(
        [TimerTrigger("0 0 20 * * 0,6")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(16, client, context);
    }

    // Age 35: 7pm Prague (18:00 UTC) on weekdays
    [Function("DailyNewspaperScheduler_Age35_Weekdays")]
    public async Task RunDailyScheduler_Age35_Weekdays(
        [TimerTrigger("0 0 18 * * 1-5")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(35, client, context);
    }

    // Age 35: 1pm Prague (12:00 UTC) on Saturday
    [Function("DailyNewspaperScheduler_Age35_Saturday")]
    public async Task RunDailyScheduler_Age35_Saturday(
        [TimerTrigger("0 0 12 * * 6")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(35, client, context);
    }

    // Age 35: 9pm Prague (20:00 UTC) on Sunday
    [Function("DailyNewspaperScheduler_Age35_Sunday")]
    public async Task RunDailyScheduler_Age35_Sunday(
        [TimerTrigger("0 0 20 * * 0")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(35, client, context);
    }

    // Age 65: 9am Prague (08:00 UTC) on weekdays
    [Function("DailyNewspaperScheduler_Age65_Weekdays")]
    public async Task RunDailyScheduler_Age65_Weekdays(
        [TimerTrigger("0 0 8 * * 1-5")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(65, client, context);
    }

    // Age 65: 1pm Prague (12:00 UTC) on Saturday
    [Function("DailyNewspaperScheduler_Age65_Saturday")]
    public async Task RunDailyScheduler_Age65_Saturday(
        [TimerTrigger("0 0 12 * * 6")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(65, client, context);
    }

    // Age 65: 4pm Prague (15:00 UTC) on Sunday
    [Function("DailyNewspaperScheduler_Age65_Sunday")]
    public async Task RunDailyScheduler_Age65_Sunday(
        [TimerTrigger("0 0 15 * * 0")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        await RunSchedulerForAge(65, client, context);
    }

    private async Task RunSchedulerForAge(int age, DurableTaskClient client, FunctionContext context)
    {
        var logger = context.GetLogger($"RunDailyScheduler_Age{age}");
        logger.LogInformation("Daily newspaper scheduler triggered for age {age} at: {time}", age, DateTime.UtcNow);

        var rssUrl = Environment.GetEnvironmentVariable("DEFAULT_RSS_URL")
            ?? throw new InvalidOperationException("DEFAULT_RSS_URL environment variable is not set");

        var request = new OrchestratorRequest
        {
            RssUrl = rssUrl,
            AudienceAge = age,
            StorageFolder = $"age-{age}/{DateTime.UtcNow:yyyy-MM-dd}"
        };

        logger.LogInformation("Starting orchestration for age {age}", age);

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(NewspaperBatchOrchestrator),
            request);

        logger.LogInformation("Started orchestration for age {age}. Instance ID: {id}", age, instanceId);
    }
}
