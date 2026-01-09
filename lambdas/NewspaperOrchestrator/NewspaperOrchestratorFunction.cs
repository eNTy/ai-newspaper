using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NewspaperOrchestrator;

public class NewspaperOrchestratorFunction
{
    private readonly ILogger<NewspaperOrchestratorFunction> _logger;

    public NewspaperOrchestratorFunction(ILogger<NewspaperOrchestratorFunction> logger)
    {
        _logger = logger;
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
        foreach (var articleUrl in topArticles.TopArticles)
        {
            var simplifyRequest = new ArticleSimplifierRequest
            {
                ArticleUrl = articleUrl,
                AudienceAge = request.AudienceAge
            };

            var task = context.CallActivityAsync<ArticleSimplifierResponse>(
                nameof(SimplifyArticle),
                simplifyRequest);

            simplifyTasks.Add(task);
        }

        var simplifiedArticles = await Task.WhenAll(simplifyTasks);
        logger.LogInformation("Simplified {count} articles", simplifiedArticles.Length);

        // Step 3: Generate images in parallel
        var imageGenTasks = new List<Task<ImageGeneratorResponse>>();
        for (int i = 0; i < simplifiedArticles.Length; i++)
        {
            var imageRequest = new ImageGeneratorRequest
            {
                ArticleTitle = simplifiedArticles[i].Title,
                SimplifiedArticle = simplifiedArticles[i].SimplifiedArticle,
                AudienceAge = request.AudienceAge,
                StorageFolder = request.StorageFolder
            };

            var task = context.CallActivityAsync<ImageGeneratorResponse>(
                nameof(GenerateImage),
                imageRequest);

            imageGenTasks.Add(task);
        }

        var images = await Task.WhenAll(imageGenTasks);
        logger.LogInformation("Generated {count} images", images.Length);

        // Step 4: Combine results
        var processedArticles = new List<ProcessedArticle>();
        for (int i = 0; i < topArticles.TopArticles.Count; i++)
        {
            processedArticles.Add(new ProcessedArticle
            {
                Url = topArticles.TopArticles[i],
                Title = simplifiedArticles[i].Title,
                SimplifiedArticle = simplifiedArticles[i].SimplifiedArticle,
                ImageUrl = images[i].ImageUrl,
                ImageDescription = images[i].Description
            });
        }

        return new BatchResult
        {
            Articles = processedArticles,
            RssUrl = request.RssUrl,
            AudienceAge = request.AudienceAge,
            ProcessedAt = DateTime.UtcNow
        };
    }

    // Activity: Fetch top articles
    [Function(nameof(FetchTopArticles))]
    public async Task<RssProcessorResponse> FetchTopArticles(
        [ActivityTrigger] RssProcessorRequest request,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(FetchTopArticles));
        logger.LogInformation("Fetching top articles from: {url}", request.RssUrl);

        var httpClientFactory = context.InstanceServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var httpClient = httpClientFactory!.CreateClient();

        var rssProcessorUrl = Environment.GetEnvironmentVariable("RSS_PROCESSOR_URL")
            ?? "http://localhost:7071/api/RssProcessor";

        var response = await httpClient.PostAsJsonAsync(rssProcessorUrl, request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RssProcessorResponse>();
        return result ?? new RssProcessorResponse();
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

        var articleSimplifierUrl = Environment.GetEnvironmentVariable("ARTICLE_SIMPLIFIER_URL")
            ?? "http://localhost:7072/api/ArticleSimplifier";

        var response = await httpClient.PostAsJsonAsync(articleSimplifierUrl, request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ArticleSimplifierResponse>();
        return result ?? new ArticleSimplifierResponse();
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

        var imageGeneratorUrl = Environment.GetEnvironmentVariable("IMAGE_GENERATOR_URL")
            ?? "http://localhost:7073/api/ImageGenerator";

        var response = await httpClient.PostAsJsonAsync(imageGeneratorUrl, request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ImageGeneratorResponse>();
        return result ?? new ImageGeneratorResponse();
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
            ?? "https://www.ceskenoviny.cz/sluzby/rss/zpravy.php";

        var storageFolder = Environment.GetEnvironmentVariable("DEFAULT_STORAGE_FOLDER")
            ?? "pipeline-runs";

        // Define the age groups to process
        var ageGroups = new[] { 8, 12, 16 };

        // Start orchestrations for each age group in parallel
        var orchestrationTasks = new List<Task<string>>();

        foreach (var age in ageGroups)
        {
            var request = new OrchestratorRequest
            {
                RssUrl = rssUrl,
                AudienceAge = age,
                StorageFolder = $"{storageFolder}/{DateTime.UtcNow:yyyy-MM-dd}/age-{age}"
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
