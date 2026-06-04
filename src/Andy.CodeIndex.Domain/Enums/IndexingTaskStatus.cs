namespace Andy.CodeIndex.Domain.Enums;

// Named IndexingTaskStatus to avoid conflict with System.Threading.Tasks.TaskStatus
public enum IndexingTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    /// <summary>
    /// Terminal status emitted by the backend watchdog when a Running task stops
    /// producing heartbeats for longer than the backstop window. The client
    /// MUST NOT infer this from its own wall-clock timer; it must read the
    /// status field from GET /api/v1/queue/{id}.
    /// </summary>
    TimedOut
}
