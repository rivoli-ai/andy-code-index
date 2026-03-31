using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Infrastructure.Services;

public class TaskQueueService : ITaskQueue
{
    private readonly IIndexingTaskRepository _taskRepo;
    private readonly IApiKeyResolver _apiKeyResolver;

    public TaskQueueService(IIndexingTaskRepository taskRepo, IApiKeyResolver apiKeyResolver)
    {
        _taskRepo = taskRepo;
        _apiKeyResolver = apiKeyResolver;
    }

    public async Task<IndexingTask> EnqueueAsync(Guid repositoryId, TaskOperation operation,
        Guid? commitId = null, int priority = 0, Guid? chainId = null,
        int? chainStepIndex = null, int? chainTotalSteps = null, CancellationToken ct = default)
    {
        // Cancel any existing Pending tasks with the same Operation + RepositoryId (deduplication)
        var duplicates = await _taskRepo.FindAsync(
            t => t.RepositoryId == repositoryId && t.Operation == operation && t.Status == IndexingTaskStatus.Pending, ct);
        foreach (var dup in duplicates)
        {
            dup.Status = IndexingTaskStatus.Cancelled;
            dup.CompletedAt = DateTime.UtcNow;
            dup.ErrorMessage = "Superseded by newer task";
        }
        if (duplicates.Count > 0)
            await _taskRepo.SaveChangesAsync(ct);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            CommitId = commitId,
            Operation = operation,
            Status = IndexingTaskStatus.Pending,
            Priority = priority,
            ChainId = chainId,
            ChainStepIndex = chainStepIndex,
            ChainTotalSteps = chainTotalSteps,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddAsync(task, ct);
        await _taskRepo.SaveChangesAsync(ct);
        return task;
    }

    public async Task<IndexingTask?> DequeueAsync(CancellationToken ct = default)
        => await _taskRepo.DequeueAsync(ct);

    public async Task UpdateStatusAsync(Guid taskId, IndexingTaskStatus status, string? errorMessage = null, CancellationToken ct = default)
        => await _taskRepo.UpdateStatusAsync(taskId, status, errorMessage, ct);

    public async Task UpdateProgressAsync(Guid taskId, int progress, string? progressMessage = null, CancellationToken ct = default)
        => await _taskRepo.UpdateProgressAsync(taskId, progress, progressMessage, ct);

    public async Task<Guid> StartChainAsync(Guid repositoryId, TaskChainType chainType, Guid? commitId = null, CancellationToken ct = default)
    {
        var chainId = Guid.NewGuid();
        var operations = GetChainOperations(chainType);

        // Enqueue first operation, rest will be chained
        if (operations.Length > 0)
        {
            await EnqueueAsync(repositoryId, operations[0], commitId, priority: 10, chainId,
                chainStepIndex: 0, chainTotalSteps: operations.Length, ct: ct);
        }

        return chainId;
    }

    public async Task EnqueueNextInChainAsync(IndexingTask completedTask, CancellationToken ct = default)
    {
        if (completedTask.ChainId is null)
            return;

        var next = GetNextOperation(completedTask.Operation);
        if (next is null)
            return;

        // Skip LLM-dependent operations only if no LLM key is available
        if (IsLlmDependent(next.Value))
        {
            var (llmKey, _, _, _) = await _apiKeyResolver.ResolveLlmKeyAsync("anonymous", ct);
            if (string.IsNullOrEmpty(llmKey))
                next = GetNextNonLlmOperation(next.Value);
        }

        if (next is null)
            return;

        var nextStepIndex = (completedTask.ChainStepIndex ?? -1) + 1;

        await EnqueueAsync(
            completedTask.RepositoryId,
            next.Value,
            completedTask.CommitId,
            priority: 5,
            completedTask.ChainId,
            chainStepIndex: nextStepIndex,
            chainTotalSteps: completedTask.ChainTotalSteps,
            ct: ct);
    }

    public async Task CancelChainAsync(Guid chainId, CancellationToken ct = default)
        => await _taskRepo.CancelChainAsync(chainId, ct);

    internal static TaskOperation[] GetChainOperations(TaskChainType chainType) => chainType switch
    {
        TaskChainType.FullIndex =>
        [
            TaskOperation.CloneRepository,
            TaskOperation.SyncRepository,
            TaskOperation.ScanCommit,
            TaskOperation.ExtractSnippets,
            TaskOperation.ExtractDocumentText,
            TaskOperation.ExtractDependencies,
            TaskOperation.ExtractCommitHistory,
            TaskOperation.CreateBM25Index,
            TaskOperation.CreateCodeEmbeddings,
            TaskOperation.CreateSummaryEnrichments,
            TaskOperation.CreateSummaryEmbeddings,
            TaskOperation.CreatePublicAPIDocs,
            // LLM-dependent (optional):
            TaskOperation.CreateArchitectureDocs,
            TaskOperation.CreateDatabaseSchema,
            TaskOperation.CreateCommitDescription,
            TaskOperation.CreateCookbook,
            TaskOperation.CreateWiki,
            TaskOperation.CreateOwnershipDocs,
            TaskOperation.CreateSecurityDocs,
            TaskOperation.CreateOperationsDocs,
            TaskOperation.CreateQualityDocs,
            TaskOperation.CreateTechStack,
            TaskOperation.CreateInsights
        ],
        TaskChainType.Resync =>
        [
            TaskOperation.SyncRepository,
            TaskOperation.ScanCommit,
            TaskOperation.ExtractSnippets,
            TaskOperation.ExtractDocumentText,
            TaskOperation.ExtractCommitHistory,
            TaskOperation.CreateBM25Index,
            TaskOperation.CreateCodeEmbeddings,
            TaskOperation.CreateSummaryEnrichments,
            TaskOperation.CreateSummaryEmbeddings,
        ],
        TaskChainType.Delete =>
        [
            TaskOperation.DeleteRepository
        ],
        _ => []
    };

    internal static TaskOperation? GetNextOperation(TaskOperation current)
    {
        // Full chain order
        TaskOperation[] fullChain =
        [
            TaskOperation.CloneRepository,
            TaskOperation.SyncRepository,
            TaskOperation.ScanCommit,
            TaskOperation.ExtractSnippets,
            TaskOperation.ExtractDocumentText,
            TaskOperation.ExtractDependencies,
            TaskOperation.ExtractCommitHistory,
            TaskOperation.CreateBM25Index,
            TaskOperation.CreateCodeEmbeddings,
            TaskOperation.CreateSummaryEnrichments,
            TaskOperation.CreateSummaryEmbeddings,
            TaskOperation.CreatePublicAPIDocs,
            TaskOperation.CreateArchitectureDocs,
            TaskOperation.CreateDatabaseSchema,
            TaskOperation.CreateCommitDescription,
            TaskOperation.CreateCookbook,
            TaskOperation.CreateWiki,
            TaskOperation.CreateOwnershipDocs,
            TaskOperation.CreateSecurityDocs,
            TaskOperation.CreateOperationsDocs,
            TaskOperation.CreateQualityDocs,
            TaskOperation.CreateTechStack,
            TaskOperation.CreateInsights,
        ];

        var index = Array.IndexOf(fullChain, current);
        if (index < 0 || index >= fullChain.Length - 1)
            return null;

        return fullChain[index + 1];
    }

    private static bool IsLlmDependent(TaskOperation op) => op is
        TaskOperation.CreateArchitectureDocs or
        TaskOperation.CreateDatabaseSchema or
        TaskOperation.CreateCommitDescription or
        TaskOperation.CreateCookbook or
        TaskOperation.CreateWiki or
        TaskOperation.CreateOwnershipDocs or
        TaskOperation.CreateSecurityDocs or
        TaskOperation.CreateOperationsDocs or
        TaskOperation.CreateQualityDocs or
        TaskOperation.CreateTechStack or
        TaskOperation.CreateInsights or
        TaskOperation.CreateSummaryEnrichments or
        TaskOperation.CreateSummaryEmbeddings;

    private static TaskOperation? GetNextNonLlmOperation(TaskOperation current)
    {
        var next = GetNextOperation(current);
        while (next.HasValue && IsLlmDependent(next.Value))
        {
            next = GetNextOperation(next.Value);
        }
        return next;
    }
}
