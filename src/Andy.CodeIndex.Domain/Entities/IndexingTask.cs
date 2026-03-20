using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Domain.Entities;

public class IndexingTask
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid? CommitId { get; set; }
    public TaskOperation Operation { get; set; }
    public IndexingTaskStatus Status { get; set; } = IndexingTaskStatus.Pending;
    public int Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? ChainId { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Repository Repository { get; set; } = null!;
}
