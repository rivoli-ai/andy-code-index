namespace Andy.CodeIndex.Domain.Entities;

public class Tag
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Name { get; set; }
    public required string CommitSha { get; set; }
    public DateTime CreatedAt { get; set; }

    public Repository Repository { get; set; } = null!;
}
