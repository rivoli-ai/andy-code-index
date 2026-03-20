using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class BackgroundWorkerServiceTests
{
    private readonly Mock<ITaskQueue> _queueMock = new();
    private readonly Mock<ITaskHandler> _handlerMock = new();

    private BackgroundWorkerService CreateService()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();

        serviceProvider.Setup(sp => sp.GetService(typeof(ITaskQueue)))
            .Returns(_queueMock.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(IEnumerable<ITaskHandler>)))
            .Returns(new[] { _handlerMock.Object });

        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new BackgroundWorkerService(
            scopeFactory.Object,
            NullLogger<BackgroundWorkerService>.Instance);
    }

    [Fact]
    public async Task Worker_DequeuesAndExecutesHandler()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.CloneRepository,
            Status = IndexingTaskStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        var callCount = 0;
        _queueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => callCount++ == 0 ? task : null);

        _handlerMock.Setup(h => h.Operation).Returns(TaskOperation.CloneRepository);
        _handlerMock.Setup(h => h.HandleAsync(task, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _handlerMock.Verify(h => h.HandleAsync(task, It.IsAny<CancellationToken>()), Times.Once);
        _queueMock.Verify(q => q.UpdateStatusAsync(task.Id, IndexingTaskStatus.Completed, null, It.IsAny<CancellationToken>()), Times.Once);
        _queueMock.Verify(q => q.EnqueueNextInChainAsync(task, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Worker_HandlerThrows_MarksTaskAsFailed()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.CloneRepository,
            Status = IndexingTaskStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        var callCount = 0;
        _queueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => callCount++ == 0 ? task : null);

        _handlerMock.Setup(h => h.Operation).Returns(TaskOperation.CloneRepository);
        _handlerMock.Setup(h => h.HandleAsync(task, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Clone failed"));

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _queueMock.Verify(q => q.UpdateStatusAsync(task.Id, IndexingTaskStatus.Failed, "Clone failed", It.IsAny<CancellationToken>()), Times.Once);
        // Should NOT chain on failure
        _queueMock.Verify(q => q.EnqueueNextInChainAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_NoHandler_MarksTaskAsFailed()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.CreateWiki, // No handler registered for this
            Status = IndexingTaskStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        var callCount = 0;
        _queueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => callCount++ == 0 ? task : null);

        _handlerMock.Setup(h => h.Operation).Returns(TaskOperation.CloneRepository); // Different operation

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _queueMock.Verify(q => q.UpdateStatusAsync(task.Id, IndexingTaskStatus.Failed,
            It.Is<string>(s => s.Contains("No handler")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Worker_NoTasks_PollsAndWaits()
    {
        _queueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexingTask?)null);

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        // Should have polled but not executed any handlers
        _handlerMock.Verify(h => h.HandleAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
