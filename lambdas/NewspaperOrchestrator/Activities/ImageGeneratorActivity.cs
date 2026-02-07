using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using OpenAI.Images;

namespace NewspaperOrchestrator.Activities;

public class ImageGeneratorActivity
{
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerConfig _containerConfig;
    private readonly IHttpClientFactory _httpClientFactory;

    public ImageGeneratorActivity(
        ChatClient chatClient,
        ImageClient imageClient,
        BlobServiceClient blobServiceClient,
        BlobContainerConfig containerConfig,
        IHttpClientFactory httpClientFactory)
    {
        _chatClient = chatClient;
        _imageClient = imageClient;
        _blobServiceClient = blobServiceClient;
        _containerConfig = containerConfig;
        _httpClientFactory = httpClientFactory;
    }

    [Function(nameof(GenerateImage))]
    public async Task<ProcessedArticle> GenerateImage(
        [ActivityTrigger] ArticleActivityInput input,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(GenerateImage));
        var article = input.Article;

        logger.LogInformation("Generating image for: {title} (age: {age})", article.Title, input.AudienceAge);

        // Step 1: Generate image prompt
        var imagePrompt = GenerateImagePrompt(article.Title, article.SimplifiedArticle, input.AudienceAge);

        // Step 2: Generate image with retry on content policy violations
        var (imageData, description) = await GenerateImageWithOpenAIAsync(imagePrompt, logger);
        logger.LogInformation("Generated image ({size} bytes)", imageData.Length);

        // Step 3: Upload to blob storage
        var imageUrl = await UploadToStorageAsync(imageData, article.StorageFolder, logger);
        logger.LogInformation("Uploaded image to: {url}", imageUrl);

        article.ImageUrl = imageUrl;
        article.ImageDescription = description;

        return article;
    }

    private static string GenerateImagePrompt(string title, string article, int audienceAge)
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

    private async Task<(byte[] ImageData, string Description)> GenerateImageWithOpenAIAsync(string imagePrompt, ILogger logger)
    {
        const int maxRetries = 3;
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            attempt++;
            try
            {
                // Use GPT-4o to refine the image prompt
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
                        Temperature = attempt == 1 ? 0.8f : 0.5f
                    });

                var refinedPrompt = chatCompletion.Value.Content[0].Text.Trim();
                logger.LogInformation("Refined image prompt (attempt {attempt}): {prompt}", attempt, refinedPrompt);

                // Generate image with DALL-E
                var imageGeneration = await _imageClient.GenerateImageAsync(
                    refinedPrompt,
                    new ImageGenerationOptions
                    {
                        Size = GeneratedImageSize.W1024xH1024,
                        Quality = GeneratedImageQuality.Standard,
                        ResponseFormat = GeneratedImageFormat.Uri
                    });

                var imageUrl = imageGeneration.Value.ImageUri;
                logger.LogInformation("Generated image URL: {url}", imageUrl);

                // Download the generated image
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(2);
                var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);

                logger.LogInformation("Downloaded image: {size} bytes", imageBytes.Length);

                return (imageBytes, refinedPrompt);
            }
            catch (Exception ex) when (IsContentPolicyViolation(ex))
            {
                lastException = ex;
                logger.LogWarning("Content policy violation on attempt {attempt}/{maxRetries}. Error: {error}",
                    attempt, maxRetries, ex.Message);

                if (attempt >= maxRetries)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate image after {maxRetries} attempts. All prompts were rejected by content policy.", ex);
                }

                await Task.Delay(1000 * attempt);
            }
        }

        throw lastException ?? new InvalidOperationException("Image generation failed for unknown reason");
    }

    private static bool IsContentPolicyViolation(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("content_policy_violation") ||
               message.Contains("content policy") ||
               (message.Contains("400") && message.Contains("safety"));
    }

    private async Task<string> UploadToStorageAsync(byte[] imageData, string storageFolder, ILogger logger)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerConfig.ContainerName);

        var blobPath = string.IsNullOrEmpty(storageFolder)
            ? "image.png"
            : $"{storageFolder}/image.png";

        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new MemoryStream(imageData);

        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "image/png"
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);

        var blobUrl = blobClient.Uri.ToString();
        logger.LogInformation("Image uploaded to: {url}", blobUrl);

        return blobUrl;
    }
}
