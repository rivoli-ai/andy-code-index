namespace Andy.CodeIndex.Domain.Enums;

// Named IndexingTaskStatus to avoid conflict with System.Threading.Tasks.TaskStatus
public enum IndexingTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
