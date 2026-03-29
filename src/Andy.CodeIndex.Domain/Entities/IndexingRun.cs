namespace Andy.CodeIndex.Domain.Entities;

public class IndexingRun
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid? ChainId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running"; // running, completed, failed
    public int SnippetsAdded { get; set; }
    public int SnippetsUpdated { get; set; }
    public int SnippetsDeleted { get; set; }
    public int SnippetsUnchanged { get; set; }
    public int ApiDocsGenerated { get; set; }
    public int CommitsScanned { get; set; }
    public int FilesFiltered { get; set; }
    public int FilesSkipped { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }

    public Repository Repository { get; set; } = null!;
}
