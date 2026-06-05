using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IIndexingTaskRepository : IRepository<IndexingTask>
{
    Task<IndexingTask?> DequeueAsync(CancellationToken ct = default);
    Task<List<IndexingTask>> GetByStatusAsync(IndexingTaskStatus status, CancellationToken ct = default);
    Task<List<IndexingTask>> GetByChainIdAsync(Guid chainId, CancellationToken ct = default);
    Task<List<IndexingTask>> GetByRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, IndexingTaskStatus status, string? errorMessage = null, CancellationToken ct = default);
    Task UpdateProgressAsync(Guid id, int progress, string? progressMessage = null, CancellationToken ct = default);
    Task CancelChainAsync(Guid chainId, CancellationToken ct = default);

    /// <summary>
    /// Refresh <c>LastHeartbeatAt</c> and advance <c>Seq</c> for a Running task to
    /// signal that the worker is still alive. Called periodically by the
    /// background worker so the watchdog can distinguish a live task from a
    /// hung one (§7.4 heartbeat contract).
    /// </summary>
    Task UpdateHeartbeatAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Flip all Running tasks whose <c>LastHeartbeatAt</c> is older than
    /// <paramref name="cutoff"/> to <see cref="IndexingTaskStatus.TimedOut"/>.
    /// Returns the IDs of tasks that were transitioned.
    /// Called by the backend watchdog; the client MUST NOT call this.
    /// </summary>
    Task<IReadOnlyList<Guid>> TimeOutStalledTasksAsync(DateTime cutoff, CancellationToken ct = default);
}
