using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

/// <summary>
/// Background watchdog that flips stalled Running tasks to
/// <c>TimedOut</c> when their <c>LastHeartbeatAt</c> has not been updated
/// for longer than <see cref="IndexingOptions.HeartbeatTimeoutMinutes"/>.
/// This is the §7.4 explicit backend-owned timeout signal: the client MUST
/// observe the <c>TimedOut</c> status from GET /api/v1/queue/{id} rather
/// than inferring failure from its own wall-clock timer.
/// </summary>
public class WatchdogService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<IndexingOptions> _options;
    private readonly ILogger<WatchdogService> _logger;

    public WatchdogService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<IndexingOptions> options,
        ILogger<WatchdogService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Watchdog started (interval: {Interval} min, timeout: {Timeout} min)",
            _options.CurrentValue.WatchdogIntervalMinutes,
            _options.CurrentValue.HeartbeatTimeoutMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = _options.CurrentValue.WatchdogIntervalMinutes;
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog sweep failed");
            }
        }

        _logger.LogInformation("Watchdog stopped");
    }

    internal async Task SweepAsync(CancellationToken ct = default)
    {
        var timeoutMinutes = _options.CurrentValue.HeartbeatTimeoutMinutes;
        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

        using var scope = _scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IIndexingTaskRepository>();
        var timedOut = await taskRepo.TimeOutStalledTasksAsync(cutoff, ct);

        if (timedOut.Count > 0)
        {
            _logger.LogWarning(
                "Watchdog timed out {Count} stalled task(s) (cutoff: {Cutoff:O}): {Ids}",
                timedOut.Count, cutoff, string.Join(", ", timedOut));
        }
    }
}
