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
    Task UpdateProgressAsync(Guid id, int progress, CancellationToken ct = default);
    Task CancelChainAsync(Guid chainId, CancellationToken ct = default);
}
