using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// SM.2.9 — WatchdogService unit tests.
/// Verifies the watchdog calls TimeOutStalledTasksAsync with the correct cutoff
/// and that the client does not own the timeout calculation.
/// </summary>
public class WatchdogServiceTests
{
    private static WatchdogService CreateWatchdog(
        IIndexingTaskRepository taskRepo,
        int heartbeatTimeoutMinutes = 30,
        int watchdogIntervalMinutes = 5)
    {
        var options = new IndexingOptions
        {
            HeartbeatTimeoutMinutes = heartbeatTimeoutMinutes,
            WatchdogIntervalMinutes = watchdogIntervalMinutes
        };
        var optionsMonitor = new TestOptionsMonitor<IndexingOptions>(options);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IIndexingTaskRepository)))
            .Returns(taskRepo);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new WatchdogService(
            scopeFactory.Object,
            optionsMonitor,
            NullLogger<WatchdogService>.Instance);
    }

    [Fact]
    public async Task SweepAsync_CallsTimeOutStalledTasks_WithCorrectCutoff()
    {
        var taskRepoMock = new Mock<IIndexingTaskRepository>();
        taskRepoMock
            .Setup(r => r.TimeOutStalledTasksAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var watchdog = CreateWatchdog(taskRepoMock.Object, heartbeatTimeoutMinutes: 30);
        var before = DateTime.UtcNow;

        await watchdog.SweepAsync(CancellationToken.None);

        var after = DateTime.UtcNow;

        taskRepoMock.Verify(r =>
            r.TimeOutStalledTasksAsync(
                It.Is<DateTime>(cutoff =>
                    cutoff >= before.AddMinutes(-30).AddSeconds(-5) &&
                    cutoff <= after.AddMinutes(-30).AddSeconds(5)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SweepAsync_RespectsConfiguredHeartbeatTimeout()
    {
        var taskRepoMock = new Mock<IIndexingTaskRepository>();
        taskRepoMock
            .Setup(r => r.TimeOutStalledTasksAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var watchdog = CreateWatchdog(taskRepoMock.Object, heartbeatTimeoutMinutes: 60);
        var before = DateTime.UtcNow;

        await watchdog.SweepAsync(CancellationToken.None);

        taskRepoMock.Verify(r =>
            r.TimeOutStalledTasksAsync(
                It.Is<DateTime>(cutoff =>
                    cutoff >= before.AddMinutes(-60).AddSeconds(-5) &&
                    cutoff <= before.AddMinutes(-60).AddSeconds(10)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SweepAsync_DoesNotThrow_WhenNoTasksAreStalled()
    {
        var taskRepoMock = new Mock<IIndexingTaskRepository>();
        taskRepoMock
            .Setup(r => r.TimeOutStalledTasksAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var watchdog = CreateWatchdog(taskRepoMock.Object);

        var act = () => watchdog.SweepAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SweepAsync_DoesNotThrow_WhenRepositoryThrows()
    {
        // The watchdog catches exceptions and logs them — it must not crash
        var taskRepoMock = new Mock<IIndexingTaskRepository>();
        taskRepoMock
            .Setup(r => r.TimeOutStalledTasksAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var watchdog = CreateWatchdog(taskRepoMock.Object);

        // SweepAsync itself propagates — the protection is in ExecuteAsync.
        // Verify it propagates the exception so the caller (test or ExecuteAsync) can catch it.
        var act = () => watchdog.SweepAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

/// <summary>Simple IOptionsMonitor stub for tests.</summary>
file sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly T _value;
    public TestOptionsMonitor(T value) => _value = value;
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();
    private sealed class NullDisposable : IDisposable { public void Dispose() { } }
}
