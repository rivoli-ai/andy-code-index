namespace Andy.CodeIndex.Application.DTOs;

public class ChatRequest
{
    public required string Message { get; set; }
    public Guid? RepositoryId { get; set; }
    public string? ConversationId { get; set; }
    /// <summary>Optional default git ref (branch, tag, or SHA) for file access context.</summary>
    public string? Ref { get; set; }
}

public class ChatResponse
{
    public required string Reply { get; set; }
    public required string ConversationId { get; set; }
    public List<ChatSource> Sources { get; set; } = [];
    public string? Model { get; set; }
}

public class ChatSource
{
    public required string FilePath { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public required string Content { get; set; }
    public string? Language { get; set; }
    public string? RepositoryName { get; set; }
    public double Score { get; set; }
    /// <summary>Git ref (branch, tag, or SHA) this source was fetched at.</summary>
    public string? Ref { get; set; }
    /// <summary>Resolved commit SHA for the ref.</summary>
    public string? ResolvedCommitSha { get; set; }
}
