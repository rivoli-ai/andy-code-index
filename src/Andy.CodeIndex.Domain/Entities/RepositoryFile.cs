namespace Andy.CodeIndex.Domain.Entities;

public class RepositoryFile
{
    public Guid Id { get; set; }
    public Guid CommitId { get; set; }
    public required string Path { get; set; }
    public string? Language { get; set; }
    public long Size { get; set; }
    public string? Hash { get; set; }
    public DateTime CreatedAt { get; set; }

    public Commit Commit { get; set; } = null!;
}
