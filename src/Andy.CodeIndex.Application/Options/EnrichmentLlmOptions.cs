namespace Andy.CodeIndex.Application.Options;

public class EnrichmentLlmOptions
{
    public const string SectionName = "Enrichment";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxRetries { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);
}
