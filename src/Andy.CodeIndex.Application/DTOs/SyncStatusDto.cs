namespace Andy.CodeIndex.Application.DTOs;

public class SyncStatusDto
{
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int RepositoriesTracked { get; set; }
}

public class IndexingRunDto
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public string Status { get; set; } = "";
    public int SnippetsAdded { get; set; }
    public int SnippetsUpdated { get; set; }
    public int SnippetsDeleted { get; set; }
    public int SnippetsUnchanged { get; set; }
    public int ApiDocsGenerated { get; set; }
    public int CommitsScanned { get; set; }
    public string? ErrorMessage { get; set; }
}
