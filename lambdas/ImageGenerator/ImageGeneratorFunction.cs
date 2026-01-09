using System.Net;
using System.Text;
using System.Text.Json;

namespace ImageGenerator;

public class ImageGeneratorFunction
{
    private readonly ILogger _logger;
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;
    private readonly string _storageConnectionString;
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

            _logger.LogInformation("Generating image for article: '{Title}' (age: {Age})",
                request.ArticleTitle, request.AudienceAge);

            // Step 1: Generate image prompt based on article
            var imagePrompt = GenerateImagePrompt(request.ArticleTitle, request.SimplifiedArticle, request.AudienceAge);
            _logger.LogInformation("Generated image prompt");

            // Step 2: Use Azure OpenAI to generate the image
            var imageData = await GenerateImageWithOpenAIAsync(imagePrompt);
            _logger.LogInformation("Generated image ({Size} bytes)", imageData.Length);

            // Step 3: Upload to Azure Storage
            var imageUrl = await UploadToAzureStorageAsync(imageData, request.StorageFolder, request.ArticleTitle);
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
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI API error: {Message}", ex.Message);
            _logger.LogWarning("Falling back to placeholder image due to error");
            return CreatePlaceholderImage($"Image generation failed: {ex.Message}");
        }
    }

    private byte[] CreatePlaceholderImage(string description)
    {
        // Create a simple SVG image as a placeholder
        // In production, replace this with actual image generation API call
        var svg = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<svg width=""800"" height=""600"" xmlns=""http://www.w3.org/2000/svg"">
  <rect width=""800"" height=""600"" fill=""#f0f0f0""/>
  <rect x=""50"" y=""50"" width=""700"" height=""500"" fill=""#ffffff"" stroke=""#cccccc"" stroke-width=""2""/>
  <text x=""400"" y=""280"" font-family=""Arial, sans-serif"" font-size=""24"" fill=""#333333"" text-anchor=""middle"">
    Generated Illustration
  </text>
  <text x=""400"" y=""320"" font-family=""Arial, sans-serif"" font-size=""16"" fill=""#666666"" text-anchor=""middle"">
    (Placeholder - Integrate with DALL-E or Stable Diffusion)
  </text>
  <foreignObject x=""100"" y=""350"" width=""600"" height=""150"">
    <div xmlns=""http://www.w3.org/1999/xhtml"" style=""font-family: Arial; font-size: 12px; color: #999; text-align: center; padding: 20px;"">
      {System.Security.SecurityElement.Escape(description.Length > 200 ? description.Substring(0, 200) + "..." : description)}
    </div>
  </foreignObject>
</svg>";

        return Encoding.UTF8.GetBytes(svg);
    }

    private async Task<string> UploadToAzureStorageAsync(byte[] imageData, string storageFolder, string articleTitle)
    {
        // Create blob service client
        var blobServiceClient = new BlobServiceClient(_storageConnectionString);

        // Get or create container
        var containerName = "article-images";
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

        // Generate unique blob name
        var sanitizedTitle = string.Join("_", articleTitle.Split(Path.GetInvalidFileNameChars()));
        if (sanitizedTitle.Length > 50) sanitizedTitle = sanitizedTitle.Substring(0, 50);

        // Detect file type (PNG for DALL-E images, SVG for placeholder)
        var isPng = imageData.Length > 4 &&
                    imageData[0] == 0x89 && imageData[1] == 0x50 &&
                    imageData[2] == 0x4E && imageData[3] == 0x47;

        var extension = isPng ? "png" : "svg";
        var contentType = isPng ? "image/png" : "image/svg+xml";

        var fileName = $"{storageFolder}/{sanitizedTitle}_{Guid.NewGuid():N}.{extension}";
        var blobClient = containerClient.GetBlobClient(fileName);

        // Upload the image
        using var stream = new MemoryStream(imageData);
        await blobClient.UploadAsync(stream, overwrite: true);

        // Set content type
        await blobClient.SetHttpHeadersAsync(new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = contentType
        });

        return blobClient.Uri.ToString();
    }
}

public class ImageGeneratorRequest
{
    public string ArticleTitle { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string StorageFolder { get; set; } = "images";
}

public class ImageGeneratorResponse
{
    public string ArticleTitle { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}
