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
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? ChainId { get; set; }
    public int? ChainStepIndex { get; set; }
    public int? ChainTotalSteps { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Monotonically-increasing watermark. Incremented on every status or
    /// progress write. Consumers compare two poll responses by their <c>seq</c>
    /// values; the response with the higher <c>seq</c> is authoritative and
    /// supersedes the lower one. This prevents stale out-of-order polls from
    /// regressing visible state (§7.3 watermark contract).
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// Timestamp of the last heartbeat written by the background worker while
    /// this task is Running. Updated roughly every heartbeat interval. If a
    /// Running task has not produced a heartbeat for longer than the backstop
    /// window (see <see cref="Andy.CodeIndex.Application.Options.IndexingOptions.HeartbeatTimeoutMinutes"/>)
    /// the watchdog flips the status to <see cref="IndexingTaskStatus.TimedOut"/>
    /// (§7.4 explicit timeout signal). Consumers MUST NOT infer TimedOut from
    /// their own wall-clock timer; they MUST read the <c>status</c> field.
    /// </summary>
    public DateTime? LastHeartbeatAt { get; set; }

    public Repository Repository { get; set; } = null!;
}
