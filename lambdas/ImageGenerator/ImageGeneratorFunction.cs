using System.Net;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using OpenAI.Images;

namespace ImageGenerator;

public class ImageGeneratorFunction
{
    private readonly ILogger _logger;
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;
    private readonly string _storageConnectionString;
    private readonly string _containerName;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ImageGeneratorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ImageGeneratorFunction>();

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");

        _chatClient = new ChatClient(model: "gpt-4o", apiKey: apiKey);
        _imageClient = new ImageClient(model: "dall-e-3", apiKey: apiKey);
        _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set");
        _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable is not set");

    }

    [Function("ImageGenerator")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Processing image generation request");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ImageGeneratorRequest>(requestBody, JsonOptions);

            if (request == null || string.IsNullOrEmpty(request.ArticleTitle) || string.IsNullOrEmpty(request.SimplifiedArticle))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid request. Please provide 'articleTitle', 'simplifiedArticle', 'audienceAge', and 'storageFolder'.");
                return badResponse;
            }

            _logger.LogInformation("Generating image for article: '{Title}' (age: {Age}), folder: '{StorageFolder}'",
                request.ArticleTitle, request.AudienceAge, request.StorageFolder);

            // Step 1: Generate image prompt based on article
            var imagePrompt = GenerateImagePrompt(request.ArticleTitle, request.SimplifiedArticle, request.AudienceAge);
            _logger.LogInformation("Generated image prompt");

            // Step 2: Use Azure OpenAI to generate the image
            var imageData = await GenerateImageWithOpenAIAsync(imagePrompt);
            _logger.LogInformation("Generated image ({Size} bytes)", imageData.Length);

            // Step 3: Upload to Azure Storage
            var imageUrl = await UploadToAzureStorageAsync(imageData, request.StorageFolder);
            _logger.LogInformation("Uploaded image to: {Url}", imageUrl);

            // Step 4: Return the image URL
            var response = req.CreateResponse(HttpStatusCode.OK);

            var result = new ImageGeneratorResponse
            {
                ArticleTitle = request.ArticleTitle,
                AudienceAge = request.AudienceAge,
                ImageUrl = imageUrl,
                StorageFolder = request.StorageFolder
            };

            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image generation");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    private string GenerateImagePrompt(string title, string article, int audienceAge)
    {
        var styleGuidance = audienceAge switch
        {
            < 8 => "colorful, cartoon-style, simple shapes, friendly and cheerful",
            < 11 => "bright and engaging, illustrated children's book style, educational",
            < 14 => "semi-realistic, dynamic and interesting, age-appropriate",
            < 18 => "realistic but engaging, modern illustration style",
            _ => "professional, realistic, journalistic style"
        };

        return $@"Create an appropriate, safe-for-work illustration for this news article aimed at {audienceAge}-year-olds.

Article Title: {title}
Article Summary: {article}

Style: {styleGuidance}

Requirements:
- Family-friendly and age-appropriate
- Clear and easy to understand
- Engaging and visually appealing
- Related to the main topic of the article
- No text or words in the image";
    }

    private async Task<byte[]> GenerateImageWithOpenAIAsync(string imagePrompt)
    {
        // Step 1: Use GPT-4o to refine the image prompt
        var refinementPrompt = $@"{imagePrompt}

Please refine this into a concise DALL-E prompt (max 400 characters) for a high-quality,
family-friendly illustration. Focus on visual elements, composition, and style.";

        var chatCompletion = await _chatClient.CompleteChatAsync(
            [new UserChatMessage(refinementPrompt)],
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = 150,
                Temperature = 0.8f
            });

        var refinedPrompt = chatCompletion.Value.Content[0].Text.Trim();

        _logger.LogInformation("Refined image prompt: {Prompt}", refinedPrompt);

        // Step 2: Generate actual image with DALL-E
        var imageGeneration = await _imageClient.GenerateImageAsync(
            refinedPrompt,
            new ImageGenerationOptions
            {
                Size = GeneratedImageSize.W1024xH1024,
                Quality = GeneratedImageQuality.Standard,
                ResponseFormat = GeneratedImageFormat.Uri
            });

        var imageUrl = imageGeneration.Value.ImageUri;

        _logger.LogInformation("Generated image URL: {Url}", imageUrl);

        // Step 3: Download the generated image
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(2);
        var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);

        _logger.LogInformation("Downloaded image: {Size} bytes", imageBytes.Length);

        return imageBytes;
    }

    private async Task<string> UploadToAzureStorageAsync(byte[] imageData, string storageFolder)
    {
        string fileName = "image.png";

        // Create blob service client
        var blobServiceClient = new BlobServiceClient(_storageConnectionString);

        // Get or create container
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

        // Use provided filename with folder path
        var blobPath = string.IsNullOrEmpty(storageFolder)
            ? fileName
            : $"{storageFolder}/{fileName}";

        var blobClient = containerClient.GetBlobClient(blobPath);

        // Upload the image (overwrite if exists)
        using var stream = new MemoryStream(imageData);
        await blobClient.UploadAsync(stream, overwrite: true);

        // Set content type
        await blobClient.SetHttpHeadersAsync(new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = "image/png"
        });

        return blobClient.Uri.ToString();       
    }
}

public class ImageGeneratorRequest
{
    public string ArticleTitle { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string StorageFolder { get; set; } = string.Empty;
}

public class ImageGeneratorResponse
{
    public string ArticleTitle { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}