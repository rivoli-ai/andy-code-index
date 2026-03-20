using Andy.CodeIndex.Application.Interfaces;
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
                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<ITaskQueue>();
                var handlers = scope.ServiceProvider.GetServices<ITaskHandler>();

                var task = await queue.DequeueAsync(stoppingToken);
                if (task is null)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Processing task {Id}: {Operation} for repo {RepoId}",
                    task.Id, task.Operation, task.RepositoryId);

                var handler = handlers.FirstOrDefault(h => h.Operation == task.Operation);
                if (handler is null)
                {
                    _logger.LogWarning("No handler registered for operation {Operation}", task.Operation);
                    await queue.UpdateStatusAsync(task.Id, IndexingTaskStatus.Failed,
                        $"No handler for {task.Operation}", stoppingToken);
                    continue;
                }

                try
                {
                    await handler.HandleAsync(task, stoppingToken);
                    await queue.UpdateStatusAsync(task.Id, IndexingTaskStatus.Completed, ct: stoppingToken);

                    _logger.LogInformation("Task {Id} completed: {Operation}", task.Id, task.Operation);

                    // Chain: enqueue next operation
                    await queue.EnqueueNextInChainAsync(task, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Task {Id} failed: {Operation}", task.Id, task.Operation);
                    await queue.UpdateStatusAsync(task.Id, IndexingTaskStatus.Failed,
                        ex.Message, stoppingToken);
                }
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
}
