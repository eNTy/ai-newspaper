using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ImageGenerator;

public class ImageGeneratorFunction
{
    private readonly ILogger _logger;
    private readonly string _claudeApiKey;
    private readonly string _storageConnectionString;
    private const string CLAUDE_API_URL = "https://api.anthropic.com/v1/messages";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ImageGeneratorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ImageGeneratorFunction>();
        _claudeApiKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY")
            ?? throw new InvalidOperationException("CLAUDE_API_KEY environment variable is not set");
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

            // Step 2: Use Claude AI to generate the image
            var imageData = await GenerateImageWithClaudeAsync(imagePrompt);
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

    private async Task<byte[]> GenerateImageWithClaudeAsync(string imagePrompt)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", _claudeApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        httpClient.Timeout = TimeSpan.FromMinutes(2); // Image generation can take longer

        // Note: Claude API doesn't natively support image generation
        // We'll use Claude to generate a detailed image description and then use a placeholder
        // In production, you would integrate with DALL-E, Stable Diffusion, or another image generation API

        var claudeRequest = new
        {
            model = "claude-sonnet-4-5-20250929",
            max_tokens = 500,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = $@"{imagePrompt}

Please provide a detailed, vivid description of an illustration that would perfectly accompany this article.
Describe colors, composition, main elements, and style in detail so an artist could create it.
Keep it appropriate for the target age group."
                }
            }
        };

        var jsonContent = JsonSerializer.Serialize(claudeRequest);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(CLAUDE_API_URL, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude API error: {StatusCode} - {ResponseBody}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Claude API returned {response.StatusCode}: {responseBody}");
        }

        var claudeResponse = JsonSerializer.Deserialize<ClaudeApiResponse>(responseBody);
        var imageDescription = claudeResponse?.Content?[0].Text ?? "A colorful illustration";

        _logger.LogInformation("Image description from Claude: {Description}", imageDescription);

        // For now, create a simple placeholder image with the description
        // In production, you would call an actual image generation API here (DALL-E, Midjourney, etc.)
        return CreatePlaceholderImage(imageDescription);
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

        var fileName = $"{storageFolder}/{sanitizedTitle}_{Guid.NewGuid():N}.svg";
        var blobClient = containerClient.GetBlobClient(fileName);

        // Upload the image
        using var stream = new MemoryStream(imageData);
        await blobClient.UploadAsync(stream, overwrite: true);

        // Set content type
        await blobClient.SetHttpHeadersAsync(new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = "image/svg+xml"
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

public class ClaudeApiResponse
{
    [JsonPropertyName("content")]
    public ClaudeContent[]? Content { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("usage")]
    public ClaudeUsage? Usage { get; set; }
}

public class ClaudeContent
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class ClaudeUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}
