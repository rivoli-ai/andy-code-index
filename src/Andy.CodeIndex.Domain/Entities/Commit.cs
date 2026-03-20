namespace Andy.CodeIndex.Domain.Entities;

public class Commit
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Sha { get; set; }
    public required string Message { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public DateTime CommittedAt { get; set; }
    public bool IsIndexed { get; set; }
    public DateTime CreatedAt { get; set; }

    public Repository Repository { get; set; } = null!;
    public ICollection<RepositoryFile> Files { get; set; } = new List<RepositoryFile>();
    public ICollection<Enrichment> Enrichments { get; set; } = new List<Enrichment>();
}
