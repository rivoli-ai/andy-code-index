namespace Andy.CodeIndex.Application.Options;

public class EmbeddingOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "text-embedding-3-small";
    public string? ApiKey { get; set; }
    public int Dimensions { get; set; } = 1536;
    public int MaxBatchSize { get; set; } = 20;
    public int MaxBatchChars { get; set; } = 16000;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 5;
    public int Parallelism { get; set; } = 1;
}
