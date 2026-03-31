using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface ITaskQueue
{
    Task<IndexingTask> EnqueueAsync(Guid repositoryId, TaskOperation operation,
        Guid? commitId = null, int priority = 0, Guid? chainId = null, CancellationToken ct = default);
    Task<IndexingTask?> DequeueAsync(CancellationToken ct = default);
    Task UpdateStatusAsync(Guid taskId, IndexingTaskStatus status, string? errorMessage = null, CancellationToken ct = default);
    Task UpdateProgressAsync(Guid taskId, int progress, string? progressMessage = null, CancellationToken ct = default);
    Task<Guid> StartChainAsync(Guid repositoryId, TaskChainType chainType, Guid? commitId = null, CancellationToken ct = default);
    Task EnqueueNextInChainAsync(IndexingTask completedTask, CancellationToken ct = default);
    Task CancelChainAsync(Guid chainId, CancellationToken ct = default);
}

public enum TaskChainType
{
    FullIndex,
    Resync,
    Delete
}
