using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    private readonly string _rssProcessorUrl;
    private readonly string _articleSimplifierUrl;
    private readonly string _imageGeneratorUrl;
    private readonly string _textToSpeechUrl;
    private readonly string _videoGeneratorUrl;
    private readonly string _storageConnectionString;
    private readonly string _blobContainerName;

    public NewspaperOrchestratorFunction(ILogger<NewspaperOrchestratorFunction> logger)
    {
        _logger = logger;

        // Fetch all required configuration at startup - fail fast if missing
        _rssProcessorUrl = Environment.GetEnvironmentVariable("RSS_PROCESSOR_URL")
            ?? throw new InvalidOperationException("RSS_PROCESSOR_URL environment variable is not set");

        _articleSimplifierUrl = Environment.GetEnvironmentVariable("ARTICLE_SIMPLIFIER_URL")
            ?? throw new InvalidOperationException("ARTICLE_SIMPLIFIER_URL environment variable is not set");

        _imageGeneratorUrl = Environment.GetEnvironmentVariable("IMAGE_GENERATOR_URL")
            ?? throw new InvalidOperationException("IMAGE_GENERATOR_URL environment variable is not set");

        _textToSpeechUrl = Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_URL")
            ?? throw new InvalidOperationException("TEXT_TO_SPEECH_URL environment variable is not set");

        _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set");

        _blobContainerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable is not set");

        _videoGeneratorUrl = Environment.GetEnvironmentVariable("VIDEO_GENERATOR_URL")
            ?? throw new InvalidOperationException("VIDEO_GENERATOR_URL environment variable is not set");
       
        _logger.LogInformation("NewspaperOrchestratorFunction initialized with all required configuration");
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

        // Step 0: Warm up the Video Generator container app (async, don't wait)
        // This triggers the container to spin up as early as possible so it's ready when we need it
        logger.LogInformation("Starting Video Generator container warmup");
        var warmupTask = context.CallActivityAsync<bool>(nameof(WarmupVideoGenerator));

        // Step 1: Fetch top 3 articles from RSS
        var rssRequest = new RssProcessorRequest
        {
            RssUrl = request.RssUrl,
            AudienceAge = request.AudienceAge
        };

        var topArticles = await context.CallActivityAsync<RssProcessorResponse>(
            nameof(FetchTopArticles),
            rssRequest);

        logger.LogInformation("Found {count} articles to process", topArticles.TopArticles.Count);

        // Step 2: Simplify articles in parallel
        var simplifyTasks = new List<Task<ArticleSimplifierResponse>>();
        foreach (var article in topArticles.TopArticles)
        {
            var simplifyRequest = new ArticleSimplifierRequest
            {
                ArticleUrl = article.Url,
                AudienceAge = request.AudienceAge
            };

            var task = context.CallActivityAsync<ArticleSimplifierResponse>(
                nameof(SimplifyArticle),
                simplifyRequest);

            simplifyTasks.Add(task);
        }

        var simplifiedArticles = await Task.WhenAll(simplifyTasks);
        logger.LogInformation("Simplified {count} articles", simplifiedArticles.Length);

        // Step 3: Generate images and audio in parallel
        var imageGenTasks = new List<Task<ImageGeneratorResponse>>();
        var audioGenTasks = new List<Task<TextToSpeechResponse>>();

        for (int i = 0; i < simplifiedArticles.Length; i++)
        {
            var storageFolder = $"{request.StorageFolder}/article-{i}";

            // Image generation task
            var imageRequest = new ImageGeneratorRequest
            {
                ArticleTitle = simplifiedArticles[i].Title,
                SimplifiedArticle = simplifiedArticles[i].SimplifiedArticle,
                AudienceAge = request.AudienceAge,
                StorageFolder = storageFolder
            };

            var imageTask = context.CallActivityAsync<ImageGeneratorResponse>(
                nameof(GenerateImage),
                imageRequest);

            imageGenTasks.Add(imageTask);

            // Audio generation task
            var audioRequest = new TextToSpeechRequest
            {
                ArticleTitle = simplifiedArticles[i].Title,
                SimplifiedArticle = simplifiedArticles[i].SimplifiedArticle,
                StorageFolder = storageFolder
            };

            var audioTask = context.CallActivityAsync<TextToSpeechResponse>(
                nameof(GenerateAudio),
                audioRequest);

            audioGenTasks.Add(audioTask);
        }

        var images = await Task.WhenAll(imageGenTasks);
        var audios = await Task.WhenAll(audioGenTasks);
        logger.LogInformation("Generated {count} images and {count} audio files", images.Length, audios.Length);

        // Step 4: Combine results and save as JSON
        var processedArticles = new List<ProcessedArticle>();
        var saveJsonTasks = new List<Task<SaveArticleJsonResponse>>();

        for (int i = 0; i < topArticles.TopArticles.Count; i++)
        {
            var storageFolder = $"{request.StorageFolder}/article-{i}";

            var article = new ProcessedArticle
            {
                Url = topArticles.TopArticles[i].Url,
                Title = simplifiedArticles[i].Title,
                SimplifiedArticle = simplifiedArticles[i].SimplifiedArticle,
                ImageUrl = images[i].ImageUrl,
                ImageDescription = images[i].Description,
                AudioUrl = audios[i].AudioUrl
            };

            processedArticles.Add(article);

            // Save the complete article data as JSON
            var saveJsonRequest = new SaveArticleJsonRequest
            {
                Article = article,
                StorageFolder = storageFolder
            };

            var saveJsonTask = context.CallActivityAsync<SaveArticleJsonResponse>(
                nameof(SaveArticleJson),
                saveJsonRequest);

            saveJsonTasks.Add(saveJsonTask);
        }

        await Task.WhenAll(saveJsonTasks);
        logger.LogInformation("Saved {count} article JSON files", processedArticles.Count);

        // Step 5: Generate videos for all articles in batch (async pattern)
        string? videoBatchResult = null;

        // Ensure the warmup task completed before generating videos
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

        // Only proceed with video generation if warmup was successful
        if (warmupSuccessful)
        {
            try
            {
                var videoRequest = new VideoGeneratorRequest
                {
                    StorageFolders = processedArticles.Select((_, i) => $"{request.StorageFolder}/article-{i}").ToArray()
                };

                // Trigger async video generation job
                var jobId = await context.CallActivityAsync<string>(
                    nameof(GenerateVideos),
                    videoRequest);

                logger.LogInformation("Video generation job started: {jobId}", jobId);

                // Poll for completion with exponential backoff
                var maxAttempts = 60; // Max 10 minutes (with increasing delays)
                var attempt = 0;
                VideoGenerationStatusResponse? statusResponse = null;

                while (attempt < maxAttempts)
                {
                    // Wait before checking status (exponential backoff: 5s, 10s, 15s, 20s, max 30s)
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
                    videoBatchResult = "Video generation status unknown";
                }
                else if (statusResponse.Status == "Completed")
                {
                    var successCount = statusResponse.Results?.Count(r => r.Success) ?? 0;
                    var totalCount = statusResponse.TotalFolders;
                    videoBatchResult = $"Generated {successCount}/{totalCount} videos successfully";
                    logger.LogInformation(videoBatchResult);

                    // Log any failures
                    var failures = statusResponse.Results?.Where(r => !r.Success).ToList() ?? new List<VideoResultItem>();
                    foreach (var failure in failures)
                    {
                        logger.LogWarning("Video generation failed for folder {folder}: {error}", failure.Folder, failure.Error);
                    }
                }
                else if (statusResponse.Status == "Failed")
                {
                    videoBatchResult = $"Video generation failed: {statusResponse.ErrorMessage}";
                    logger.LogWarning(videoBatchResult);
                }
                else if (attempt >= maxAttempts)
                {
                    videoBatchResult = $"Video generation timed out after {maxAttempts} attempts (status: {statusResponse.Status})";
                    logger.LogWarning(videoBatchResult);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Video generation failed, continuing without videos");
                videoBatchResult = $"Video generation failed: {ex.Message}";
            }
        }
        else
        {
            logger.LogInformation("Skipping video generation (warmup was not successful)");
            videoBatchResult = "Video generation skipped (warmup unsuccessful or URL not configured)";
        }

        return new BatchResult
        {
            Articles = processedArticles,
            RssUrl = request.RssUrl,
            AudienceAge = request.AudienceAge,
            ProcessedAt = DateTime.UtcNow,
            VideoGenerationResult = videoBatchResult
        };
    }

    // Activity: Fetch top articles
    [Function(nameof(FetchTopArticles))]
    public async Task<RssProcessorResponse> FetchTopArticles(
        [ActivityTrigger] RssProcessorRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(FetchTopArticles));
        logger.LogInformation("Fetching top articles from: {url} for age: {age}", request.RssUrl, request.AudienceAge);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(_rssProcessorUrl, content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RssProcessorResponse>();
            return result ?? new RssProcessorResponse();
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to call RSS Processor. Status: {status}. " +
                "Ensure RSS_PROCESSOR_URL is set and function key is available (via Key Vault or URL parameter)",
                ex.StatusCode);
            throw;
        }
    }

    // Activity: Simplify article
    [Function(nameof(SimplifyArticle))]
    public async Task<ArticleSimplifierResponse> SimplifyArticle(
        [ActivityTrigger] ArticleSimplifierRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SimplifyArticle));
        logger.LogInformation("Simplifying article: {url}", request.ArticleUrl);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(_articleSimplifierUrl, content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ArticleSimplifierResponse>();
            return result ?? new ArticleSimplifierResponse();
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to call Article Simplifier. Status: {status}. " +
                "Ensure ARTICLE_SIMPLIFIER_URL is set and function key is available (via Key Vault or URL parameter)",
                ex.StatusCode);
            throw;
        }
    }

    // Activity: Generate image
    [Function(nameof(GenerateImage))]
    public async Task<ImageGeneratorResponse> GenerateImage(
        [ActivityTrigger] ImageGeneratorRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(GenerateImage));
        logger.LogInformation("Generating image for: {title}", request.ArticleTitle);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(_imageGeneratorUrl, content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ImageGeneratorResponse>();
            return result ?? new ImageGeneratorResponse();
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to call Image Generator. Status: {status}. " +
                "Ensure IMAGE_GENERATOR_URL is set and function key is available (via Key Vault or URL parameter)",
                ex.StatusCode);
            throw;
        }
    }

    // Activity: Generate audio
    [Function(nameof(GenerateAudio))]
    public async Task<TextToSpeechResponse> GenerateAudio(
        [ActivityTrigger] TextToSpeechRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(GenerateAudio));
        logger.LogInformation("Generating audio for: {title}", request.ArticleTitle);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        var requestJson = JsonSerializer.Serialize(request);
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(_textToSpeechUrl, content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TextToSpeechResponse>();
            return result ?? new TextToSpeechResponse();
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to call Text-to-Speech. Status: {status}. " +
                "Ensure TEXT_TO_SPEECH_URL is set and function key is available (via Key Vault or URL parameter)",
                ex.StatusCode);
            throw;
        }
    }

    // Activity: Warm up the Video Generator container app
    [Function(nameof(WarmupVideoGenerator))]
    public async Task<bool> WarmupVideoGenerator(
        [ActivityTrigger] FunctionContext context)
    {
        var logger = context.GetLogger(nameof(WarmupVideoGenerator));

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        try
        {
            // Call the health endpoint to trigger container spin-up
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
            // Don't fail the orchestration if warmup fails - it's just an optimization
            logger.LogWarning(ex, "Video Generator container warmup failed (non-critical): {message}", ex.Message);
            return false;
        }
    }

    // Activity: Trigger async video generation job
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
            logger.LogError(ex, "Failed to trigger video generation. Status: {status}. " +
                "Ensure VIDEO_GENERATOR_URL is set and Container App is accessible",
                ex.StatusCode);
            throw;
        }
    }

    // Activity: Check video generation job status
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
            logger.LogError(ex, "Failed to check video generation status. Status: {status}",
                ex.StatusCode);
            throw;
        }
    }

    // Activity: Save article JSON to storage
    [Function(nameof(SaveArticleJson))]
    public async Task<SaveArticleJsonResponse> SaveArticleJson(
        [ActivityTrigger] SaveArticleJsonRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SaveArticleJson));
        logger.LogInformation("Saving article JSON for: {title}", request.Article.Title);

        try
        {
            var blobServiceClient = new BlobServiceClient(_storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(_blobContainerName);

            var blobPath = $"{request.StorageFolder}/article.json";
            var blobClient = containerClient.GetBlobClient(blobPath);

            // Serialize the article to JSON
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var jsonContent = JsonSerializer.Serialize(request.Article, jsonOptions);
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
            logger.LogInformation("Saved article JSON to: {Url}", blobUrl);

            return new SaveArticleJsonResponse
            {
                JsonUrl = blobUrl
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save article JSON. Ensure AzureWebJobsStorage and BLOB_CONTAINER_NAME are set");
            throw;
        }
    }

    // HTTP endpoint to check orchestration status
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

    // Timer trigger - runs daily at 5AM
    [Function("DailyNewspaperScheduler")]
    public async Task RunDailyScheduler(
        [TimerTrigger("0 0 5 * * *")] TimerInfo timerInfo,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(RunDailyScheduler));
        logger.LogInformation("Daily newspaper scheduler triggered at: {time}", DateTime.UtcNow);

        var rssUrl = Environment.GetEnvironmentVariable("DEFAULT_RSS_URL")
            ?? throw new InvalidOperationException("DEFAULT_RSS_URL environment variable is not set");

        // Define the age groups to process
        var ageGroups = new[] { 8, 12,16 };

        // Start orchestrations for each age group in parallel
        var orchestrationTasks = new List<Task<string>>();

        foreach (var age in ageGroups)
        {
            var request = new OrchestratorRequest
            {
                RssUrl = rssUrl,
                AudienceAge = age,
                StorageFolder = $"age-{age}/{DateTime.UtcNow:yyyy-MM-dd}"
            }; 

            logger.LogInformation("Starting orchestration for age {age}", age);

            var task = client.ScheduleNewOrchestrationInstanceAsync(
                nameof(NewspaperBatchOrchestrator),
                request);

            orchestrationTasks.Add(task);
        }

        var instanceIds = await Task.WhenAll(orchestrationTasks);

        logger.LogInformation("Started {count} orchestrations. Instance IDs: {ids}",
            instanceIds.Length,
            string.Join(", ", instanceIds));
    }
}
