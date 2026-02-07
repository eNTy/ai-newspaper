using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace NewspaperOrchestrator.Activities;

public class TextToSpeechActivity
{
    private readonly AudioClient _audioClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerConfig _containerConfig;

    public TextToSpeechActivity(AudioClient audioClient, BlobServiceClient blobServiceClient, BlobContainerConfig containerConfig)
    {
        _audioClient = audioClient;
        _blobServiceClient = blobServiceClient;
        _containerConfig = containerConfig;
    }

    [Function(nameof(GenerateAudio))]
    public async Task<ProcessedArticle> GenerateAudio(
        [ActivityTrigger] ArticleActivityInput input,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(GenerateAudio));
        var article = input.Article;

        logger.LogInformation("Generating audio for: {title}", article.Title);

        // Step 1: Generate speech
        var textToSpeak = $"{article.Title}. {article.SimplifiedArticle}";
        logger.LogInformation("Generating speech for {length} characters of text", textToSpeak.Length);

        var speechGeneration = await _audioClient.GenerateSpeechAsync(
            textToSpeak,
            GeneratedSpeechVoice.Nova,
            new SpeechGenerationOptions
            {
                ResponseFormat = GeneratedSpeechFormat.Mp3,
                SpeedRatio = 1.1f
            });

        var audioBytes = speechGeneration.Value.ToArray();
        logger.LogInformation("Generated audio: {size} bytes", audioBytes.Length);

        // Step 2: Upload to blob storage
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerConfig.ContainerName);

        var blobPath = string.IsNullOrEmpty(article.StorageFolder)
            ? "speech.mp3"
            : $"{article.StorageFolder}/speech.mp3";

        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new MemoryStream(audioBytes);

        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "audio/mpeg"
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);

        var audioUrl = blobClient.Uri.ToString();
        logger.LogInformation("Audio uploaded to: {url}", audioUrl);

        article.AudioUrl = audioUrl;

        return article;
    }
}
