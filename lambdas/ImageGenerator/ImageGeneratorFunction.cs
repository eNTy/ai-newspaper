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
        const int maxRetries = 3;
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            attempt++;
            try
            {
                // Step 1: Use GPT-4o to refine the image prompt
                var refinementPrompt = attempt == 1
                    ? $@"{imagePrompt}

Please refine this into a concise DALL-E prompt (max 400 characters) for a high-quality,
family-friendly illustration. Focus on visual elements, composition, and style."
                    : $@"{imagePrompt}

Please refine this into a concise DALL-E prompt (max 400 characters) for a high-quality,
family-friendly illustration. Focus on visual elements, composition, and style.

IMPORTANT: The previous attempt was rejected by content policy. Make this version MORE ABSTRACT and GENERIC.
- Use symbolic or metaphorical representations instead of specific people or events
- Focus on general concepts, objects, or nature scenes related to the topic
- Avoid any potentially controversial elements
- Keep it simple and universally appropriate";

                var chatCompletion = await _chatClient.CompleteChatAsync(
                    [new UserChatMessage(refinementPrompt)],
                    new ChatCompletionOptions
                    {
                        MaxOutputTokenCount = 150,
                        Temperature = attempt == 1 ? 0.8f : 0.5f // Lower temperature on retries for safer prompts
                    });

                var refinedPrompt = chatCompletion.Value.Content[0].Text.Trim();

                _logger.LogInformation("Refined image prompt (attempt {Attempt}): {Prompt}", attempt, refinedPrompt);

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
            catch (Exception ex) when (IsContentPolicyViolation(ex))
            {
                lastException = ex;
                _logger.LogWarning("Content policy violation on attempt {Attempt}/{MaxRetries}. Error: {Error}. Will retry with adjusted prompt.",
                    attempt, maxRetries, ex.Message);

                if (attempt >= maxRetries)
                {
                    _logger.LogError("Failed to generate image after {MaxRetries} attempts due to content policy violations", maxRetries);
                    throw new InvalidOperationException(
                        $"Failed to generate image after {maxRetries} attempts. All prompts were rejected by content policy.", ex);
                }

                // Wait a bit before retrying
                await Task.Delay(1000 * attempt);
            }
        }

        // This should never be reached, but just in case
        throw lastException ?? new InvalidOperationException("Image generation failed for unknown reason");
    }

    private static bool IsContentPolicyViolation(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("content_policy_violation") ||
               message.Contains("content policy") ||
               (message.Contains("400") && message.Contains("safety"));
    }

    private async Task<string> UploadToAzureStorageAsync(byte[] imageData, string storageFolder)
    {
        string fileName = "image.png";

        _logger.LogInformation("Uploading {Size} bytes to storage folder '{Folder}'", imageData.Length, storageFolder);

        var blobServiceClient = new BlobServiceClient(_storageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

        _logger.LogInformation("Got container client for '{Container}'", _containerName);

        var blobPath = string.IsNullOrEmpty(storageFolder)
            ? fileName
            : $"{storageFolder}/{fileName}";

        _logger.LogInformation("Blob path: {Path}", blobPath);

        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new MemoryStream(imageData);
        _logger.LogInformation("Starting upload to '{Path}'...", blobPath);

        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "image/png"
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);
        _logger.LogInformation("Successfully uploaded blob to '{Path}'", blobPath);

        var blobUrl = blobClient.Uri.ToString();
        _logger.LogInformation("Image uploaded successfully: {Url}", blobUrl);

        return blobUrl;
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