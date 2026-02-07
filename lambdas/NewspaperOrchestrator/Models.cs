using System.Text.Json.Serialization;

namespace NewspaperOrchestrator;

public class OrchestrationStepException : Exception
{
    public string StepName { get; }

    public OrchestrationStepException(string stepName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StepName = stepName;
    }
}

public class OrchestratorRequest
{
    public string RssUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string StorageFolder { get; set; } = string.Empty;
}

public class ProcessedArticle
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ImageDescription { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}

public class OrchestratorResponse
{
    public string InstanceId { get; set; } = string.Empty;
    public string StatusQueryUrl { get; set; } = string.Empty;
}

public class BatchResult
{
    public bool Success { get; set; }
    public string? FailedStep { get; set; }
    public string? ErrorMessage { get; set; }
    public List<ProcessedArticle> Articles { get; set; } = new();
    public string RssUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string? Result { get; set; }
    public DateTime BatchStartTime { get; set; }
    public DateTime? BatchEndTime { get; set; }
    public TimeSpan? BatchDuration { get; set; }
    public string? StorageFolder { get; set; }
    public string? InstagramMediaId { get; set; }
    public string? InstagramUrl { get; set; }
}

// Lightweight input for per-article activities (SimplifyArticle, GenerateImage, GenerateAudio)
public class ArticleActivityInput
{
    public ProcessedArticle Article { get; set; } = new();
    public int AudienceAge { get; set; }
}

// Internal model for RSS feed parsing
public class RssFeedItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTime PublishDate { get; set; }
    public List<string> Categories { get; set; } = new();
}

// DI config record for blob container name
public record BlobContainerConfig(string ContainerName);

// Video Generator Container App models
public class VideoGeneratorRequest
{
    public string[]? StorageFolders { get; set; }
}

public class VideoResultItem
{
    public string Folder { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? VideoUrl { get; set; }
    public string? Error { get; set; }
}

// Request for checking video generation status
public class CheckVideoStatusRequest
{
    public string JobId { get; set; } = string.Empty;
}

// Response from Container App async trigger
public class VideoGenerationJobResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

// Response from Container App status query
public class VideoGenerationStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalFolders { get; set; }
    public int ProcessedFolders { get; set; }
    public List<VideoResultItem>? Results { get; set; }
    public string? ErrorMessage { get; set; }
}

// Instagram publish result
public class InstagramPublishResult
{
    public string MediaId { get; set; } = string.Empty;
    public string? Permalink { get; set; }
}

// Instagram Graph API response models
public class GraphApiIdResponse
{
    public string Id { get; set; } = string.Empty;
}

public class ContainerStatusResponse
{
    [JsonPropertyName("status_code")]
    public string StatusCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
