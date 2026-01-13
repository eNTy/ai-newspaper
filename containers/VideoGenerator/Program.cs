using Azure.Storage.Blobs;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddHttpClient();

// Add Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry();

// Global semaphore to ensure only one video generation at a time
// This prevents memory exhaustion from concurrent FFMPEG processes
var videoGenerationSemaphore = new SemaphoreSlim(1, 1);

// In-memory job store for tracking async video generation jobs
var jobStore = new ConcurrentDictionary<string, VideoGenerationJob>();

var app = builder.Build();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Video generation endpoint - triggers generation and returns job ID
app.MapPost("/api/generate", (VideoGenerationRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation("Received async video generation request for {Count} folders", request.StorageFolders?.Length ?? 0);

    if (request.StorageFolders == null || request.StorageFolders.Length == 0)
    {
        logger.LogWarning("No storage folders provided in request");
        return Results.BadRequest(new { error = "StorageFolders array is required and cannot be empty" });
    }

    // Create a new job
    var jobId = Guid.NewGuid().ToString();
    var job = new VideoGenerationJob
    {
        JobId = jobId,
        StorageFolders = request.StorageFolders,
        Status = JobStatus.Queued,
        CreatedAt = DateTime.UtcNow,
        TotalFolders = request.StorageFolders.Length,
        ProcessedFolders = 0
    };

    jobStore[jobId] = job;
    logger.LogInformation("Created job {JobId} for {Count} folders", jobId, request.StorageFolders.Length);

    // Start background processing
    _ = Task.Run(async () =>
    {
        if (!jobStore.TryGetValue(jobId, out var bgJob))
        {
            logger.LogError("Job {JobId} not found in store", jobId);
            return;
        }

        logger.LogInformation("Starting background processing for job {JobId}", jobId);

        try
        {
            bgJob.Status = JobStatus.Processing;

            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable not set");

            var containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
                ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable not set");

            foreach (var storageFolder in bgJob.StorageFolders)
            {
                logger.LogInformation("Processing folder: {StorageFolder} for job {JobId}", storageFolder, jobId);

                try
                {
                    // Acquire semaphore to ensure only one video generation at a time
                    logger.LogInformation("Waiting for video generation slot for job {JobId}...", jobId);
                    await videoGenerationSemaphore.WaitAsync();

                    try
                    {
                        logger.LogInformation("Starting video generation for folder: {StorageFolder} (job {JobId})", storageFolder, jobId);
                        var videoUrl = await GenerateVideoAsync(storageFolder, storageConnectionString, containerName, logger);

                        bgJob.Results.Add(new VideoGenerationResult
                        {
                            Folder = storageFolder,
                            Success = true,
                            VideoUrl = videoUrl
                        });

                        logger.LogInformation("Successfully generated video for folder: {StorageFolder} (job {JobId})", storageFolder, jobId);
                    }
                    finally
                    {
                        // Always release semaphore
                        videoGenerationSemaphore.Release();
                        logger.LogInformation("Released video generation slot for job {JobId}", jobId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to generate video for folder: {StorageFolder} (job {JobId})", storageFolder, jobId);
                    bgJob.Results.Add(new VideoGenerationResult
                    {
                        Folder = storageFolder,
                        Success = false,
                        Error = ex.Message
                    });
                }
                finally
                {
                    bgJob.ProcessedFolders++;
                }
            }

            bgJob.Status = JobStatus.Completed;
            bgJob.CompletedAt = DateTime.UtcNow;

            var successCount = bgJob.Results.Count(r => r.Success);
            logger.LogInformation("Completed job {JobId}. Success: {SuccessCount}/{TotalCount}",
                jobId, successCount, bgJob.Results.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed with exception", jobId);
            bgJob.Status = JobStatus.Failed;
            bgJob.CompletedAt = DateTime.UtcNow;
            bgJob.ErrorMessage = ex.Message;
        }
    });

    return Results.Accepted($"/api/generate/status/{jobId}", new
    {
        jobId,
        status = job.Status.ToString(),
        message = "Job queued successfully. Use the jobId to check status."
    });
});

// Status query endpoint - check job progress and get results
app.MapGet("/api/generate/status/{jobId}", (string jobId, ILogger<Program> logger) =>
{
    logger.LogInformation("Status check requested for job {JobId}", jobId);

    if (!jobStore.TryGetValue(jobId, out var job))
    {
        logger.LogWarning("Job not found: {JobId}", jobId);
        return Results.NotFound(new { error = $"Job {jobId} not found" });
    }

    var response = new
    {
        jobId = job.JobId,
        status = job.Status.ToString(),
        createdAt = job.CreatedAt,
        completedAt = job.CompletedAt,
        totalFolders = job.TotalFolders,
        processedFolders = job.ProcessedFolders,
        results = job.Results,
        errorMessage = job.ErrorMessage
    };

    logger.LogInformation("Job {JobId} status: {Status}, Processed: {Processed}/{Total}",
        jobId, job.Status, job.ProcessedFolders, job.TotalFolders);

    return job.Status == JobStatus.Completed || job.Status == JobStatus.Failed
        ? Results.Ok(response)
        : Results.Ok(response);
});

app.Run();

static async Task<string> GenerateVideoAsync(string storageFolder, string storageConnectionString, string containerName, ILogger logger)
{
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
            logger.LogInformation("Deleted existing output file: {OutputPath}", outputPath);
        }

        logger.LogInformation("Downloading files from Azure Storage...");
        var blobServiceClient = new BlobServiceClient(storageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        await DownloadBlobAsync(containerClient, $"{storageFolder}/image.png", imagePath, logger);
        await DownloadBlobAsync(containerClient, $"{storageFolder}/speech.mp3", audioPath, logger);

        bool hasArticleJson = await DownloadBlobAsync(containerClient, $"{storageFolder}/article.json", articlePath, logger);

        string? articleTitle = null;
        if (hasArticleJson)
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(articlePath);
                var metadata = JsonSerializer.Deserialize<ArticleMetadata>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                articleTitle = metadata?.Title;
                logger.LogInformation("Extracted article title: {Title}", articleTitle ?? "(none)");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract article title from JSON");
            }
        }

        var audioDuration = await GetAudioDurationAsync(audioPath, logger);
        logger.LogInformation("Audio duration detected: {Duration} seconds", audioDuration);

        // Log audio file details
        var audioFileInfo = new FileInfo(audioPath);
        logger.LogInformation("Audio file size: {Size} bytes", audioFileInfo.Length);

        logger.LogInformation("Generating video with FFMPEG...");
        await GenerateVideoWithFFMPEGAsync(imagePath, audioPath, outputPath, audioDuration, articleTitle, logger);

        logger.LogInformation("Uploading video to Azure Storage...");
        var videoUrl = await UploadVideoToStorageAsync(outputPath, storageFolder, containerClient, logger);

        logger.LogInformation("Video generated successfully: {VideoUrl}", videoUrl);
        return videoUrl;
    }
    finally
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up temporary directory: {TempDir}", tempDir);
        }
    }
}

static async Task<bool> DownloadBlobAsync(BlobContainerClient containerClient, string blobPath, string localPath, ILogger logger)
{
    try
    {
        var blobClient = containerClient.GetBlobClient(blobPath);
        var exists = await blobClient.ExistsAsync();

        if (!exists)
        {
            logger.LogWarning("Blob not found: {BlobPath}", blobPath);
            return false;
        }

        logger.LogInformation("Downloading blob: {BlobPath} to {LocalPath}", blobPath, localPath);
        await blobClient.DownloadToAsync(localPath);
        return true;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to download blob: {BlobPath}", blobPath);
        throw;
    }
}

static async Task<double> GetAudioDurationAsync(string audioPath, ILogger logger)
{
    logger.LogInformation("Getting audio duration for: {AudioPath}", audioPath);

    var startInfo = new ProcessStartInfo
    {
        FileName = "ffprobe",
        Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    logger.LogInformation("Running ffprobe: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);

    using var process = Process.Start(startInfo);
    if (process == null)
    {
        throw new InvalidOperationException("Failed to start ffprobe process");
    }

    var output = await process.StandardOutput.ReadToEndAsync();
    var errorOutput = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    logger.LogInformation("ffprobe exit code: {ExitCode}", process.ExitCode);
    logger.LogInformation("ffprobe output: '{Output}'", output.Trim());

    if (!string.IsNullOrEmpty(errorOutput))
    {
        logger.LogWarning("ffprobe stderr: {Error}", errorOutput);
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"ffprobe failed: {errorOutput}");
    }

    if (double.TryParse(output.Trim(), out var duration))
    {
        logger.LogInformation("Parsed audio duration: {Duration} seconds", duration);
        return duration;
    }

    throw new InvalidOperationException($"Failed to parse audio duration: {output}");
}

static async Task GenerateVideoWithFFMPEGAsync(string imagePath, string audioPath, string outputPath, double audioDuration, string? articleTitle, ILogger logger)
{
    const int fps = 25;
    var totalFrames = (int)Math.Ceiling(audioDuration * fps);

    // Calculate zoom increment to reach 1.5x over the duration
    var zoomIncrement = 0.5 / totalFrames; // From 1.0 to 1.5 over entire duration

    // Calculate pan speed for smooth panning
    var panSpeed = 0.5;

    // Use moderate resolution input for smoother effects (2x instead of 3x to reduce memory usage)
    var zoomScale = "scale=2160:2700:force_original_aspect_ratio=increase,crop=2160:2700"; // Scale up 2x

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

    logger.LogInformation("Using effect type: {EffectType} (0=zoom in, 1=zoom out, 2=pan left, 3=pan right), total frames: {TotalFrames}, duration: {Duration}s", effectType, totalFrames, audioDuration);

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

    var arguments = $"-y -threads 2 -loop 1 -i \"{imagePath}\" -i \"{audioPath}\" -filter_complex \"{filterComplex}\" -map \"[final]\" -map 1:a -c:v libx264 -preset faster -crf 23 -c:a aac -b:a 192k -t {audioDuration} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";

    logger.LogInformation("FFMPEG command: ffmpeg {Arguments}", arguments);

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
        throw new InvalidOperationException("Failed to start ffmpeg process");
    }

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();

    await process.WaitForExitAsync();

    var stdOutput = await outputTask;
    var stdError = await errorTask;

    logger.LogInformation("FFMPEG exit code: {ExitCode}", process.ExitCode);

    if (!string.IsNullOrEmpty(stdError))
    {
        logger.LogInformation("FFMPEG stderr (may contain progress info): {StdError}", stdError.Substring(0, Math.Min(500, stdError.Length)) + "...");
    }

    if (process.ExitCode != 0)
    {
        logger.LogError("FFMPEG failed with exit code {ExitCode}", process.ExitCode);
        logger.LogError("FFMPEG full stderr: {StdError}", stdError);
        throw new InvalidOperationException($"FFMPEG failed with exit code {process.ExitCode}: {stdError}");
    }

    // Check output file
    if (File.Exists(outputPath))
    {
        var outputFileInfo = new FileInfo(outputPath);
        logger.LogInformation("FFMPEG video generation completed successfully. Output file size: {Size} bytes", outputFileInfo.Length);

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
                logger.LogInformation("Generated video duration: {VideoDuration} seconds (expected: {AudioDuration} seconds)", videoDuration, audioDuration);
            }
        }
    }
    else
    {
        logger.LogError("Output file does not exist: {OutputPath}", outputPath);
        throw new InvalidOperationException("FFMPEG did not create output file");
    }
}

static async Task<string> UploadVideoToStorageAsync(string videoPath, string storageFolder, BlobContainerClient containerClient, ILogger logger)
{
    var blobPath = $"{storageFolder}/video.mp4";
    var blobClient = containerClient.GetBlobClient(blobPath);

    logger.LogInformation("Uploading video to blob: {BlobPath}", blobPath);

    // Upload with overwrite enabled
    await blobClient.UploadAsync(videoPath, overwrite: true);

    logger.LogInformation("Video uploaded successfully to: {Uri}", blobClient.Uri);
    return blobClient.Uri.ToString();
}

static string WrapText(string text, int maxCharsPerLine)
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

// Request/Response models
public record VideoGenerationRequest
{
    public string[]? StorageFolders { get; init; }
}

public record VideoGenerationResult
{
    public required string Folder { get; init; }
    public required bool Success { get; init; }
    public string? VideoUrl { get; init; }
    public string? Error { get; init; }
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

// Async job tracking models
public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class VideoGenerationJob
{
    public string JobId { get; set; } = string.Empty;
    public string[] StorageFolders { get; set; } = Array.Empty<string>();
    public JobStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<VideoGenerationResult> Results { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public int TotalFolders { get; set; }
    public int ProcessedFolders { get; set; }
}
