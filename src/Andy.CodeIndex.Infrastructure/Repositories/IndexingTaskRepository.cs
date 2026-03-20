using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Repositories;

public class IndexingTaskRepository : RepositoryBase<IndexingTask>, IIndexingTaskRepository
{
    public IndexingTaskRepository(CodeIndexDbContext context) : base(context) { }

    public async Task<IndexingTask?> DequeueAsync(CancellationToken ct = default)
    {
        // Dequeue highest priority pending task (FIFO within same priority)
        var task = await DbSet
            .Where(t => t.Status == IndexingTaskStatus.Pending)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return null;

        task.Status = IndexingTaskStatus.Running;
        task.StartedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);

        return task;
    }

    public async Task<List<IndexingTask>> GetByStatusAsync(IndexingTaskStatus status, CancellationToken ct = default)
        => await DbSet
            .Where(t => t.Status == status)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<IndexingTask>> GetByChainIdAsync(Guid chainId, CancellationToken ct = default)
        => await DbSet
            .Where(t => t.ChainId == chainId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<IndexingTask>> GetByRepositoryAsync(Guid repositoryId, CancellationToken ct = default)
        => await DbSet
            .Where(t => t.RepositoryId == repositoryId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task UpdateStatusAsync(Guid id, IndexingTaskStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        var task = await DbSet.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Task {id} not found");

        task.Status = status;
        task.ErrorMessage = errorMessage;

        if (status == IndexingTaskStatus.Running)
            task.StartedAt = DateTime.UtcNow;

        if (status is IndexingTaskStatus.Completed or IndexingTaskStatus.Failed or IndexingTaskStatus.Cancelled)
            task.CompletedAt = DateTime.UtcNow;

        await Context.SaveChangesAsync(ct);
    }

    public async Task UpdateProgressAsync(Guid id, int progress, CancellationToken ct = default)
    {
        var task = await DbSet.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Task {id} not found");

        task.Progress = progress;
        await Context.SaveChangesAsync(ct);
    }

    public async Task CancelChainAsync(Guid chainId, CancellationToken ct = default)
    {
        var pendingTasks = await DbSet
            .Where(t => t.ChainId == chainId && t.Status == IndexingTaskStatus.Pending)
            .ToListAsync(ct);

        foreach (var task in pendingTasks)
        {
            task.Status = IndexingTaskStatus.Cancelled;
            task.CompletedAt = DateTime.UtcNow;
        }

        await Context.SaveChangesAsync(ct);
    }
}
