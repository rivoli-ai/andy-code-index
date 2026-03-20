namespace Andy.CodeIndex.Domain.Entities;

public class Branch
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Name { get; set; }
    public string? HeadCommitSha { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }

    public Repository Repository { get; set; } = null!;
}
