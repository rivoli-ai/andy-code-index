using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class TaskQueueServiceTests
{
    private readonly Mock<IIndexingTaskRepository> _taskRepoMock = new();
    private readonly Mock<IApiKeyResolver> _resolverMock = new();
    private readonly TaskQueueService _service;

    public TaskQueueServiceTests()
    {
        // Default: no LLM key available (skip LLM operations)
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<(string?, string, string)>((null, "gpt-4o-mini", "none")));
        _service = new TaskQueueService(_taskRepoMock.Object, _resolverMock.Object);

        _taskRepoMock.Setup(r => r.AddAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexingTask t, CancellationToken _) => t);
    }

    [Fact]
    public async Task EnqueueAsync_CreatesTaskWithCorrectFields()
    {
        var repoId = Guid.NewGuid();
        var chainId = Guid.NewGuid();

        var result = await _service.EnqueueAsync(repoId, TaskOperation.ScanCommit, priority: 5, chainId: chainId);

        result.RepositoryId.Should().Be(repoId);
        result.Operation.Should().Be(TaskOperation.ScanCommit);
        result.Status.Should().Be(IndexingTaskStatus.Pending);
        result.Priority.Should().Be(5);
        result.ChainId.Should().Be(chainId);

        _taskRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartChainAsync_FullIndex_EnqueuesCloneAsFirst()
    {
        var repoId = Guid.NewGuid();

        var chainId = await _service.StartChainAsync(repoId, TaskChainType.FullIndex);

        chainId.Should().NotBeEmpty();
        _taskRepoMock.Verify(r => r.AddAsync(
            It.Is<IndexingTask>(t =>
                t.Operation == TaskOperation.CloneRepository &&
                t.ChainId == chainId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartChainAsync_Resync_EnqueuesSyncAsFirst()
    {
        var repoId = Guid.NewGuid();

        await _service.StartChainAsync(repoId, TaskChainType.Resync);

        _taskRepoMock.Verify(r => r.AddAsync(
            It.Is<IndexingTask>(t => t.Operation == TaskOperation.SyncRepository),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartChainAsync_Delete_EnqueuesDeleteAsFirst()
    {
        var repoId = Guid.NewGuid();

        await _service.StartChainAsync(repoId, TaskChainType.Delete);

        _taskRepoMock.Verify(r => r.AddAsync(
            It.Is<IndexingTask>(t => t.Operation == TaskOperation.DeleteRepository),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueNextInChainAsync_CloneCompleted_EnqueuesSync()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.CloneRepository,
            Status = IndexingTaskStatus.Completed,
            ChainId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        await _service.EnqueueNextInChainAsync(task);

        _taskRepoMock.Verify(r => r.AddAsync(
            It.Is<IndexingTask>(t =>
                t.Operation == TaskOperation.SyncRepository &&
                t.ChainId == task.ChainId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueNextInChainAsync_ScanCompleted_EnqueuesExtractSnippets()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.ScanCommit,
            Status = IndexingTaskStatus.Completed,
            ChainId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        await _service.EnqueueNextInChainAsync(task);

        _taskRepoMock.Verify(r => r.AddAsync(
            It.Is<IndexingTask>(t => t.Operation == TaskOperation.ExtractSnippets),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueNextInChainAsync_NoChainId_DoesNothing()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.CloneRepository,
            ChainId = null,
            CreatedAt = DateTime.UtcNow
        };

        await _service.EnqueueNextInChainAsync(task);

        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnqueueNextInChainAsync_LastOperation_DoesNothing()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Operation = TaskOperation.CreateWiki,
            ChainId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        await _service.EnqueueNextInChainAsync(task);

        _taskRepoMock.Verify(r => r.AddAsync(It.IsAny<IndexingTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Chain operation tests
    [Theory]
    [InlineData(TaskOperation.CloneRepository, TaskOperation.SyncRepository)]
    [InlineData(TaskOperation.SyncRepository, TaskOperation.ScanCommit)]
    [InlineData(TaskOperation.ScanCommit, TaskOperation.ExtractSnippets)]
    [InlineData(TaskOperation.ExtractSnippets, TaskOperation.ExtractDependencies)]
    [InlineData(TaskOperation.ExtractDependencies, TaskOperation.ExtractCommitHistory)]
    [InlineData(TaskOperation.ExtractCommitHistory, TaskOperation.CreateBM25Index)]
    [InlineData(TaskOperation.CreateBM25Index, TaskOperation.CreateCodeEmbeddings)]
    [InlineData(TaskOperation.CreateCodeEmbeddings, TaskOperation.CreateSummaryEnrichments)]
    [InlineData(TaskOperation.CreatePublicAPIDocs, TaskOperation.CreateArchitectureDocs)]
    public void GetNextOperation_ReturnsCorrectNext(TaskOperation current, TaskOperation expectedNext)
    {
        TaskQueueService.GetNextOperation(current).Should().Be(expectedNext);
    }

    [Fact]
    public void GetNextOperation_LastInChain_ReturnsNull()
    {
        TaskQueueService.GetNextOperation(TaskOperation.CreateQualityDocs).Should().BeNull();
    }

    [Fact]
    public void GetNextOperation_DeleteRepository_ReturnsNull()
    {
        TaskQueueService.GetNextOperation(TaskOperation.DeleteRepository).Should().BeNull();
    }

    [Fact]
    public void GetChainOperations_FullIndex_HasAllSteps()
    {
        var ops = TaskQueueService.GetChainOperations(TaskChainType.FullIndex);
        ops.Should().HaveCountGreaterThanOrEqualTo(9);
        ops[0].Should().Be(TaskOperation.CloneRepository);
        ops.Should().Contain(TaskOperation.CreateCodeEmbeddings);
        ops.Should().Contain(TaskOperation.CreatePublicAPIDocs);
    }

    [Fact]
    public void GetChainOperations_Resync_StartsWithSync()
    {
        var ops = TaskQueueService.GetChainOperations(TaskChainType.Resync);
        ops[0].Should().Be(TaskOperation.SyncRepository);
        ops.Should().NotContain(TaskOperation.CloneRepository);
    }

    [Fact]
    public void GetChainOperations_Delete_OnlyHasDelete()
    {
        var ops = TaskQueueService.GetChainOperations(TaskChainType.Delete);
        ops.Should().HaveCount(1);
        ops[0].Should().Be(TaskOperation.DeleteRepository);
    }
}
