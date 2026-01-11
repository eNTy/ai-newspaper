namespace NewspaperOrchestrator;

public class OrchestratorRequest
{
    public string RssUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public string StorageFolder { get; set; } = string.Empty;
}

public class RssProcessorRequest
{
    public string RssUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
}

public class RssProcessorResponse
{
    public string SourceUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public List<ArticleResult> TopArticles { get; set; } = new();
}

public class ArticleResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class ArticleSimplifierRequest
{
    public string ArticleUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
}

public class ArticleSimplifierResponse
{
    public string Title { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
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
    public string ImageUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class TextToSpeechRequest
{
    public string ArticleTitle { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
    public string StorageFolder { get; set; } = string.Empty;
}

public class TextToSpeechResponse
{
    public string AudioUrl { get; set; } = string.Empty;
}

public class ProcessedArticle
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SimplifiedArticle { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ImageDescription { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
}

public class OrchestratorResponse
{
    public string InstanceId { get; set; } = string.Empty;
    public string StatusQueryUrl { get; set; } = string.Empty;
}

public class BatchResult
{
    public List<ProcessedArticle> Articles { get; set; } = new();
    public string RssUrl { get; set; } = string.Empty;
    public int AudienceAge { get; set; }
    public DateTime ProcessedAt { get; set; }
}
