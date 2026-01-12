using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace VideoGenerator;

public class VideoGeneratorFunction
{
    private readonly ILogger _logger;
    private readonly string _storageConnectionString;
    private readonly string _containerName;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VideoGeneratorFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<VideoGeneratorFunction>();

        _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set.");

        _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable is not set.");
    }

    [Function("VideoGenerator")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        _logger.LogInformation("VideoGenerator function triggered.");

        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<VideoGeneratorRequest>(body, JsonOptions);

            if (request == null || string.IsNullOrEmpty(request.StorageFolder))
            {
                _logger.LogWarning("Invalid request: StorageFolder is required.");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("StorageFolder is required.");
                return badResponse;
            }

            _logger.LogInformation($"Processing video generation for folder: {request.StorageFolder}");

            var videoUrl = await GenerateVideoAsync(request.StorageFolder);

            var result = new VideoGeneratorResponse
            {
                StorageFolder = request.StorageFolder,
                VideoUrl = videoUrl
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in VideoGenerator function.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Internal server error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<string> GenerateVideoAsync(string storageFolder)
    {
        var blobServiceClient = new BlobServiceClient(_storageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var imagePath = Path.Combine(tempDir, "image.png");
            var audioPath = Path.Combine(tempDir, "speech.mp3");
            var articlePath = Path.Combine(tempDir, "article.json");
            var outputPath = Path.Combine(tempDir, "video.mp4");

            // Delete existing output file if it exists to ensure clean generation
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
                _logger.LogInformation($"Deleted existing output file: {outputPath}");
            }

            _logger.LogInformation("Downloading files from Azure Storage...");
            await DownloadBlobAsync(containerClient, $"{storageFolder}/image.png", imagePath);
            await DownloadBlobAsync(containerClient, $"{storageFolder}/speech.mp3", audioPath);

            bool hasArticleJson = await DownloadBlobAsync(containerClient, $"{storageFolder}/article.json", articlePath);

            string? articleTitle = null;
            if (hasArticleJson)
            {
                try
                {
                    var articleJson = await File.ReadAllTextAsync(articlePath);
                    var articleData = JsonSerializer.Deserialize<ArticleMetadata>(articleJson, JsonOptions);
                    articleTitle = articleData?.Title;
                    _logger.LogInformation($"Article title extracted: {articleTitle}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract article title from JSON.");
                }
            }

            var audioDuration = await GetAudioDurationAsync(audioPath);
            _logger.LogInformation($"Audio duration detected: {audioDuration} seconds");

            // Log audio file details
            var audioFileInfo = new FileInfo(audioPath);
            _logger.LogInformation($"Audio file size: {audioFileInfo.Length} bytes");

            _logger.LogInformation("Generating video with FFMPEG...");
            await GenerateVideoWithFFMPEGAsync(imagePath, audioPath, outputPath, audioDuration, articleTitle);

            _logger.LogInformation("Uploading video to Azure Storage...");
            var videoUrl = await UploadVideoToStorageAsync(outputPath, storageFolder);

            _logger.LogInformation($"Video generated successfully: {videoUrl}");
            return videoUrl;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
                _logger.LogInformation("Temporary files cleaned up.");
            }
        }
    }

    private async Task<bool> DownloadBlobAsync(BlobContainerClient containerClient, string blobPath, string localPath)
    {
        try
        {
            var blobClient = containerClient.GetBlobClient(blobPath);
            if (!await blobClient.ExistsAsync())
            {
                _logger.LogWarning($"Blob not found: {blobPath}");
                return false;
            }

            await blobClient.DownloadToAsync(localPath);
            _logger.LogInformation($"Downloaded: {blobPath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to download blob: {blobPath}");
            throw;
        }
    }

    private async Task<double> GetAudioDurationAsync(string audioPath)
    {
        _logger.LogInformation($"Getting audio duration for: {audioPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation($"Running ffprobe: {startInfo.FileName} {startInfo.Arguments}");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start ffprobe process.");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var errorOutput = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        _logger.LogInformation($"ffprobe exit code: {process.ExitCode}");
        _logger.LogInformation($"ffprobe output: '{output.Trim()}'");

        if (!string.IsNullOrEmpty(errorOutput))
        {
            _logger.LogWarning($"ffprobe stderr: {errorOutput}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed: {errorOutput}");
        }

        if (double.TryParse(output.Trim(), out var duration))
        {
            _logger.LogInformation($"Parsed audio duration: {duration} seconds");
            return duration;
        }

        throw new InvalidOperationException($"Failed to parse audio duration: {output}");
    }

    private async Task GenerateVideoWithFFMPEGAsync(string imagePath, string audioPath, string outputPath, double audioDuration, string? articleTitle)
    {
        const int fps = 25;
        var totalFrames = (int)Math.Ceiling(audioDuration * fps);

        // Calculate zoom increment to reach 1.5x over the duration
        var zoomIncrement = 0.5 / totalFrames; // From 1.0 to 1.5 over entire duration

        // Calculate pan speed for smooth panning
        var panSpeed = 0.5;

        // Use higher resolution input for smoother effects
        var zoomScale = "scale=3240:4050:force_original_aspect_ratio=increase,crop=3240:4050"; // Scale up 3x

        // Randomly select an effect
        var random = new Random();
        var effectType = random.Next(4); // 0=zoom in, 1=zoom out, 2=pan left, 3=pan right

        string zoomPanFilter = effectType switch
        {
            // Use d=1 for frame-by-frame processing, smooth interpolation
            0 => $"[0:v]{zoomScale},zoompan=z='if(lte(on,0),1,min(1+on*{zoomIncrement:F8},1.5))':d=1:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s=1080x1350:fps={fps}[zoomed]", // Zoom in
            1 => $"[0:v]{zoomScale},zoompan=z='if(lte(on,0),1.5,max(1.5-on*{zoomIncrement:F8},1))':d=1:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s=1080x1350:fps={fps}[zoomed]", // Zoom out
            2 => $"[0:v]{zoomScale},zoompan=z='1.3':d=1:x='iw/2-(iw/zoom/2)-on*{panSpeed:F4}':y='ih/2-(ih/zoom/2)':s=1080x1350:fps={fps}[zoomed]", // Pan left to right
            3 => $"[0:v]{zoomScale},zoompan=z='1.3':d=1:x='iw/2-(iw/zoom/2)+on*{panSpeed:F4}':y='ih/2-(ih/zoom/2)':s=1080x1350:fps={fps}[zoomed]", // Pan right to left
            _ => $"[0:v]{zoomScale},zoompan=z='if(lte(on,0),1,min(1+on*{zoomIncrement:F8},1.5))':d=1:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s=1080x1350:fps={fps}[zoomed]"
        };

        _logger.LogInformation($"Using effect type: {effectType} (0=zoom in, 1=zoom out, 2=pan left, 3=pan right), total frames: {totalFrames}, duration: {audioDuration}s");

        string filterComplex;
        if (!string.IsNullOrEmpty(articleTitle))
        {
            // Wrap text to fit within video width (approximately 20-25 chars per line for font size 50)
            var wrappedTitle = WrapText(articleTitle, 25);

            var escapedTitle = wrappedTitle
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace(":", "\\:");

            // Use larger text with line spacing and center alignment
            // x=(w-text_w)/2 centers each line horizontally
            filterComplex = $"{zoomPanFilter};[zoomed]drawtext=fontfile=/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf:text='{escapedTitle}':fontcolor=white:fontsize=50:box=1:boxcolor=black@0.7:boxborderw=10:x=(w-text_w)/2:y=100:line_spacing=10[final]";
        }
        else
        {
            filterComplex = $"{zoomPanFilter};[zoomed]null[final]";
        }

        var arguments = $"-y -loop 1 -i \"{imagePath}\" -i \"{audioPath}\" -filter_complex \"{filterComplex}\" -map \"[final]\" -map 1:a -c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -t {audioDuration} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";

        _logger.LogInformation($"FFMPEG command: ffmpeg {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start ffmpeg process.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdOutput = await outputTask;
        var stdError = await errorTask;

        _logger.LogInformation($"FFMPEG exit code: {process.ExitCode}");

        if (!string.IsNullOrEmpty(stdError))
        {
            _logger.LogInformation($"FFMPEG stderr (may contain progress info): {stdError.Substring(0, Math.Min(500, stdError.Length))}...");
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError($"FFMPEG failed with exit code {process.ExitCode}");
            _logger.LogError($"FFMPEG full stderr: {stdError}");
            throw new InvalidOperationException($"FFMPEG failed with exit code {process.ExitCode}: {stdError}");
        }

        // Check output file
        if (File.Exists(outputPath))
        {
            var outputFileInfo = new FileInfo(outputPath);
            _logger.LogInformation($"FFMPEG video generation completed successfully. Output file size: {outputFileInfo.Length} bytes");

            // Use ffprobe to verify output video duration
            var verifyStartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{outputPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var verifyProcess = Process.Start(verifyStartInfo);
            if (verifyProcess != null)
            {
                var verifyOutput = await verifyProcess.StandardOutput.ReadToEndAsync();
                await verifyProcess.WaitForExitAsync();
                if (double.TryParse(verifyOutput.Trim(), out var videoDuration))
                {
                    _logger.LogInformation($"Generated video duration: {videoDuration} seconds (expected: {audioDuration} seconds)");
                }
            }
        }
        else
        {
            _logger.LogError($"Output file does not exist: {outputPath}");
            throw new InvalidOperationException("FFMPEG did not create output file");
        }
    }

    private async Task<string> UploadVideoToStorageAsync(string videoPath, string storageFolder)
    {
        var blobServiceClient = new BlobServiceClient(_storageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

        var blobPath = $"{storageFolder}/video.mp4";
        var blobClient = containerClient.GetBlobClient(blobPath);

        _logger.LogInformation($"Uploading video to blob: {blobPath}");

        // Upload with overwrite enabled
        await blobClient.UploadAsync(videoPath, overwrite: true);

        _logger.LogInformation($"Video uploaded successfully to: {blobClient.Uri}");
        return blobClient.Uri.ToString();
    }

    private static string WrapText(string text, int maxCharsPerLine)
    {
        if (text.Length <= maxCharsPerLine)
            return text;

        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = "";

        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(currentLine))
            {
                currentLine = word;
            }
            else if ((currentLine + " " + word).Length <= maxCharsPerLine)
            {
                currentLine += " " + word;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return string.Join("\n", lines);
    }
}

public class VideoGeneratorRequest
{
    public string StorageFolder { get; set; } = string.Empty;
}

public class VideoGeneratorResponse
{
    public string StorageFolder { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
}

public class ArticleMetadata
{
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? SimplifiedArticle { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageDescription { get; set; }
    public string? AudioUrl { get; set; }
}
