namespace Andy.CodeIndex.Application.Options;

public class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "text-embedding-3-small";
    public string? ApiKey { get; set; }
    public int Dimensions { get; set; } = 1536;
    public int MaxBatchSize { get; set; } = 20;
    public int MaxBatchChars { get; set; } = 16000;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 5;
    public int Parallelism { get; set; } = 1;

    /// <summary>
    /// Model dimensions lookup for known models.
    /// </summary>
    public static readonly Dictionary<string, int> ModelDimensions = new()
    {
        ["text-embedding-3-small"] = 1536,
        ["text-embedding-3-large"] = 3072,
        ["text-embedding-ada-002"] = 1536,
    };

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// Get dimensions for the configured model, falling back to the explicit Dimensions setting.
    /// </summary>
    public int GetDimensions() =>
        ModelDimensions.TryGetValue(Model, out var dims) ? dims : Dimensions;
}
