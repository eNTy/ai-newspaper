using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace TextToSpeech;

public class TextToSpeechFunction
{
    private readonly ILogger _logger;
    private readonly AudioClient _audioClient;
    private readonly string _storageConnectionString;
    private readonly string _containerName;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TextToSpeechFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TextToSpeechFunction>();

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");

        _audioClient = new AudioClient(model: "tts-1", apiKey: apiKey);
        _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set");
        _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable is not set");
    }

    [Function("TextToSpeech")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Processing text-to-speech request");

        try
        {
            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<TextToSpeechRequest>(requestBody, JsonOptions);

            if (request == null || string.IsNullOrEmpty(request.ArticleTitle) || string.IsNullOrEmpty(request.SimplifiedArticle))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid request. Please provide 'articleTitle', 'simplifiedArticle', and 'storageFolder'.");
                return badResponse;
            }

            _logger.LogInformation("Generating speech for article: '{Title}', folder: '{StorageFolder}'",
                request.ArticleTitle, request.StorageFolder);

            // Step 1: Generate speech using OpenAI TTS with Czech language
            var audioData = await GenerateSpeechWithOpenAIAsync(request.ArticleTitle, request.SimplifiedArticle);
            _logger.LogInformation("Generated speech audio ({Size} bytes)", audioData.Length);

            // Step 2: Upload to Azure Storage
            var audioUrl = await UploadToAzureStorageAsync(audioData, request.StorageFolder);
            _logger.LogInformation("Uploaded audio to: {Url}", audioUrl);

            // Step 3: Return the audio URL
            var response = req.CreateResponse(HttpStatusCode.OK);

            var result = new TextToSpeechResponse
            {
                ArticleTitle = request.ArticleTitle,
                AudioUrl = audioUrl,
                StorageFolder = request.StorageFolder
            };

            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing text-to-speech generation");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<byte[]> GenerateSpeechWithOpenAIAsync(string title, string article)
    {
        // Prepare the text for speech synthesis
        // Format it as a news article with title first, then the content
        var textToSpeak = $"{title}. {article}";

        _logger.LogInformation("Generating speech for {Length} characters of text", textToSpeak.Length);

        // Generate speech using OpenAI TTS
        // Using "nova" voice which is calm and clear
        var speechGeneration = await _audioClient.GenerateSpeechAsync(
            textToSpeak,
            GeneratedSpeechVoice.Nova,
            new SpeechGenerationOptions
            {
                ResponseFormat = GeneratedSpeechFormat.Mp3,
                SpeedRatio = 1.1f // 10% faster than normal speed
            });

        _logger.LogInformation("Speech generation completed");

        // Convert the BinaryData to byte array
        var audioBytes = speechGeneration.Value.ToArray();

        _logger.LogInformation("Generated audio: {Size} bytes", audioBytes.Length);

        return audioBytes;
    }

    private async Task<string> UploadToAzureStorageAsync(byte[] audioData, string storageFolder)
    {
        string fileName = "speech.mp3";

        _logger.LogInformation("Uploading {Size} bytes to storage folder '{Folder}'", audioData.Length, storageFolder);

        var blobServiceClient = new BlobServiceClient(_storageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

        _logger.LogInformation("Got container client for '{Container}'", _containerName);

        var blobPath = string.IsNullOrEmpty(storageFolder)
            ? fileName
            : $"{storageFolder}/{fileName}";

        _logger.LogInformation("Blob path: {Path}", blobPath);

        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new MemoryStream(audioData);
        _logger.LogInformation("Starting upload to '{Path}'...", blobPath);

        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "audio/mpeg"
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: default);
        _logger.LogInformation("Successfully uploaded blob to '{Path}'", blobPath);

        var blobUrl = blobClient.Uri.ToString();
        _logger.LogInformation("Audio uploaded successfully: {Url}", blobUrl);

        return blobUrl;
    }
}

public class TextToSpeechRequest
{
    public string ArticleTitle { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}

public class TextToSpeechResponse
{
    public string ArticleTitle { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}
