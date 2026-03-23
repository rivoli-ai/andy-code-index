namespace Andy.CodeIndex.Domain.Entities;

public class UserSettings
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public string? EmbeddingApiKey { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmApiKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
