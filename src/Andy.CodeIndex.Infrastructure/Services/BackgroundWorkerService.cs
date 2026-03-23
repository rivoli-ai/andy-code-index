using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Services;

public class BackgroundWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundWorkerService> _logger;

    public BackgroundWorkerService(
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextTaskAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background worker");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("Background worker stopped");
    }

    private async Task ProcessNextTaskAsync(CancellationToken ct)
    {
        IndexingTask? task;

        // Dequeue in its own scope
        using (var scope = _scopeFactory.CreateScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<ITaskQueue>();
            task = await queue.DequeueAsync(ct);
        }

        if (task is null)
        {
            await Task.Delay(1000, ct);
            return;
        }

        _logger.LogInformation("Processing task {Id}: {Operation} for repo {RepoId}",
            task.Id, task.Operation, task.RepositoryId);

        // Execute handler in a fresh scope
        try
        {
            using var handlerScope = _scopeFactory.CreateScope();
            var handlers = handlerScope.ServiceProvider.GetServices<ITaskHandler>();
            var handler = handlers.FirstOrDefault(h => h.Operation == task.Operation);

            if (handler is null)
            {
                _logger.LogWarning("No handler registered for operation {Operation}", task.Operation);
                await UpdateTaskStatusAsync(task.Id, IndexingTaskStatus.Failed,
                    $"No handler for {task.Operation}", ct);
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var activity = Telemetry.CodeIndexTelemetry.ActivitySource.StartActivity($"Task:{task.Operation}");
            activity?.SetTag("task.id", task.Id.ToString());
            activity?.SetTag("task.operation", task.Operation.ToString());
            activity?.SetTag("task.repository_id", task.RepositoryId.ToString());

            await handler.HandleAsync(task, ct);

            sw.Stop();
            Telemetry.CodeIndexTelemetry.TasksCompleted.Add(1,
                new KeyValuePair<string, object?>("operation", task.Operation.ToString()));
            Telemetry.CodeIndexTelemetry.TaskDuration.Record(sw.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", task.Operation.ToString()));

            // Success — mark completed and chain next in a fresh scope
            using var successScope = _scopeFactory.CreateScope();
            var successQueue = successScope.ServiceProvider.GetRequiredService<ITaskQueue>();
            await successQueue.UpdateStatusAsync(task.Id, IndexingTaskStatus.Completed, ct: ct);
            _logger.LogInformation("Task {Id} completed: {Operation}", task.Id, task.Operation);
            await successQueue.EnqueueNextInChainAsync(task, ct);

            // If chain is finished (no next operation), mark repo as indexed
            if (task.Operation != TaskOperation.DeleteRepository)
            {
                await MarkRepoAsIndexedIfChainDoneAsync(task, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Task {Id} failed: {Operation}", task.Id, task.Operation);
            Telemetry.CodeIndexTelemetry.TasksFailed.Add(1,
                new KeyValuePair<string, object?>("operation", task.Operation.ToString()));
            await UpdateTaskStatusAsync(task.Id, IndexingTaskStatus.Failed, ex.Message, ct);
        }
    }

    private async Task UpdateTaskStatusAsync(Guid taskId, IndexingTaskStatus status, string? errorMessage, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ITaskQueue>();
            await queue.UpdateStatusAsync(taskId, status, errorMessage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update task {Id} status to {Status}", taskId, status);
        }
    }

    private async Task MarkRepoAsIndexedIfChainDoneAsync(IndexingTask task, CancellationToken ct)
    {
        try
        {
            // Check if there are any pending/running tasks for this repo's chain
            using var scope = _scopeFactory.CreateScope();
            var taskRepo = scope.ServiceProvider.GetRequiredService<IIndexingTaskRepository>();

            if (task.ChainId is null) return;

            var hasPending = await taskRepo.ExistsAsync(
                t => t.ChainId == task.ChainId &&
                     (t.Status == IndexingTaskStatus.Pending || t.Status == IndexingTaskStatus.Running), ct);

            if (!hasPending)
            {
                var context = scope.ServiceProvider.GetRequiredService<Data.CodeIndexDbContext>();
                var repo = await context.Repositories.FindAsync([task.RepositoryId], ct);
                if (repo is not null && repo.Status != "indexed")
                {
                    repo.Status = "indexed";
                    repo.UpdatedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync(ct);
                    _logger.LogInformation("Repository {Name} is now indexed", repo.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check/update indexed status for repo {RepoId}", task.RepositoryId);
        }
    }
}
