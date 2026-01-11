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

        try
        {
            // Validate inputs
            if (imageData == null || imageData.Length == 0)
            {
                throw new ArgumentException("Image data is null or empty", nameof(imageData));
            }

            _logger.LogInformation("Uploading {Size} bytes to storage folder '{Folder}'", imageData.Length, storageFolder);

            // Validate connection string
            if (string.IsNullOrEmpty(_storageConnectionString))
            {
                throw new InvalidOperationException("Storage connection string is not configured. Check AzureWebJobsStorage environment variable.");
            }

            // Create blob service client
            BlobServiceClient blobServiceClient;
            try
            {
                blobServiceClient = new BlobServiceClient(_storageConnectionString);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Invalid storage connection string format");
                throw new InvalidOperationException("Storage connection string has invalid format. Check AzureWebJobsStorage environment variable.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create BlobServiceClient");
                throw new InvalidOperationException("Failed to create blob storage client. Check AzureWebJobsStorage configuration.", ex);
            }

            // Get or create container
            BlobContainerClient containerClient;
            try
            {
                containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                _logger.LogInformation("Got container client for '{Container}'", _containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get container client for '{Container}'", _containerName);
                throw new InvalidOperationException($"Failed to access blob container '{_containerName}'. Check BLOB_CONTAINER_NAME configuration.", ex);
            }

            // Create container if it doesn't exist
            try
            {
                var createResponse = await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);
                if (createResponse != null && createResponse.Value != null)
                {
                    _logger.LogInformation("Created new container '{Container}'", _containerName);
                }
                else
                {
                    _logger.LogInformation("Container '{Container}' already exists", _containerName);
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 403)
            {
                _logger.LogError(ex, "Access denied when creating container. Check storage account permissions.");
                throw new InvalidOperationException($"Access denied to storage container '{_containerName}'. The function may need 'Storage Blob Data Contributor' role.", ex);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409)
            {
                // Container already exists, this is fine
                _logger.LogInformation("Container '{Container}' already exists (409 conflict)", _containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create or access container '{Container}'", _containerName);
                throw new InvalidOperationException($"Failed to create or access blob container '{_containerName}'.", ex);
            }

            // Use provided filename with folder path
            var blobPath = string.IsNullOrEmpty(storageFolder)
                ? fileName
                : $"{storageFolder}/{fileName}";

            _logger.LogInformation("Blob path: {Path}", blobPath);

            BlobClient blobClient;
            try
            {
                blobClient = containerClient.GetBlobClient(blobPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get blob client for path '{Path}'", blobPath);
                throw new InvalidOperationException($"Failed to access blob at path '{blobPath}'.", ex);
            }

            // Upload the image (overwrite if exists)
            try
            {
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
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 403)
            {
                _logger.LogError(ex, "Access denied when uploading blob. Check storage account permissions.");
                throw new InvalidOperationException("Access denied when uploading image to storage. The function may need 'Storage Blob Data Contributor' role.", ex);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 413)
            {
                _logger.LogError(ex, "Blob too large ({Size} bytes)", imageData.Length);
                throw new InvalidOperationException($"Image size ({imageData.Length} bytes) exceeds storage limits.", ex);
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure storage request failed with status {Status}: {Message}", ex.Status, ex.Message);
                throw new InvalidOperationException($"Azure storage upload failed (status {ex.Status}): {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during blob upload");
                throw new InvalidOperationException("Failed to upload image to storage.", ex);
            }

            var blobUrl = blobClient.Uri.ToString();
            _logger.LogInformation("Image uploaded successfully: {Url}", blobUrl);

            return blobUrl;
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
        {
            _logger.LogError(ex, "Unexpected error in UploadToAzureStorageAsync");
            throw new InvalidOperationException("An unexpected error occurred while uploading image to storage.", ex);
        }
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
