using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class PeriodicSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PeriodicSyncService> _logger;
    private readonly SyncOptions _options;

    public PeriodicSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<PeriodicSyncService> logger,
        IOptions<SyncOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Periodic sync is disabled");
            return;
        }

        _logger.LogInformation("Periodic sync started with interval {Interval}s", _options.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);

            try
            {
                await SyncAllRepositoriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic sync");
            }
        }

        _logger.LogInformation("Periodic sync stopped");
    }

    internal async Task SyncAllRepositoriesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repoRepo = scope.ServiceProvider.GetRequiredService<ICodeRepositoryRepository>();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IIndexingTaskRepository>();

        var repos = await repoRepo.GetByStatusAsync("indexed", ct);
        _logger.LogInformation("Periodic sync: queueing sync for {Count} repositories", repos.Count);

        foreach (var repo in repos)
        {
            var hasPending = await taskRepo.ExistsAsync(
                t => t.RepositoryId == repo.Id &&
                     t.Operation == TaskOperation.SyncRepository &&
                     t.Status == IndexingTaskStatus.Pending, ct);

            if (hasPending)
            {
                _logger.LogDebug("Skipping sync for {Name}: sync already pending", repo.Name);
                continue;
            }

            await taskRepo.AddAsync(new IndexingTask
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Operation = TaskOperation.SyncRepository,
                Status = IndexingTaskStatus.Pending,
                ChainId = Guid.NewGuid(),
                Priority = 1,
                CreatedAt = DateTime.UtcNow
            }, ct);

            _logger.LogInformation("Queued sync for {Name}", repo.Name);
        }

        await taskRepo.SaveChangesAsync(ct);
    }
}
