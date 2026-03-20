using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class PeriodicSyncServiceTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IIndexingTaskRepository> _taskRepoMock = new();

    private PeriodicSyncService CreateService(SyncOptions? options = null)
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();

        serviceProvider.Setup(sp => sp.GetService(typeof(ICodeRepositoryRepository)))
            .Returns(_repoRepoMock.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(IIndexingTaskRepository)))
            .Returns(_taskRepoMock.Object);

        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new PeriodicSyncService(
            scopeFactory.Object,
            NullLogger<PeriodicSyncService>.Instance,
            Options.Create(options ?? new SyncOptions { Enabled = true, IntervalSeconds = 1800 }));
    }

    [Fact]
    public async Task SyncAllRepositories_QueuesSyncForIndexedRepos()
    {
        var repo1 = new Repository
        {
            Id = Guid.NewGuid(), Name = "repo1", Url = "https://a.com/1",
            Provider = GitProvider.GitHub, Status = "indexed",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var repo2 = new Repository
        {
            Id = Guid.NewGuid(), Name = "repo2", Url = "https://a.com/2",
            Provider = GitProvider.GitHub, Status = "indexed",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        _repoRepoMock.Setup(r => r.GetByStatusAsync("indexed", It.IsAny<CancellationToken>()))
            .ReturnsAsync([repo1, repo2]);
        _taskRepoMock.Setup(t => t.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<IndexingTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();
        await service.SyncAllRepositoriesAsync(CancellationToken.None);

        _taskRepoMock.Verify(t => t.AddAsync(
            It.Is<IndexingTask>(task => task.Operation == TaskOperation.SyncRepository),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _taskRepoMock.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAllRepositories_SkipsReposWithPendingSync()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(), Name = "repo1", Url = "https://a.com/1",
            Provider = GitProvider.GitHub, Status = "indexed",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        _repoRepoMock.Setup(r => r.GetByStatusAsync("indexed", It.IsAny<CancellationToken>()))
            .ReturnsAsync([repo]);
        _taskRepoMock.Setup(t => t.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<IndexingTask, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already has pending sync

        var service = CreateService();
        await service.SyncAllRepositoriesAsync(CancellationToken.None);

        _taskRepoMock.Verify(t => t.AddAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAllRepositories_NoIndexedRepos_DoesNothing()
    {
        _repoRepoMock.Setup(r => r.GetByStatusAsync("indexed", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = CreateService();
        await service.SyncAllRepositoriesAsync(CancellationToken.None);

        _taskRepoMock.Verify(t => t.AddAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Disabled_ReturnsImmediately()
    {
        var service = CreateService(new SyncOptions { Enabled = false });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Should return without waiting for interval
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        _repoRepoMock.Verify(r => r.GetByStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
