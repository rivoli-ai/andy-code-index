using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Repositories;

public class IndexingTaskRepositoryTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly IndexingTaskRepository _repo;
    private readonly Repository _testRepo;

    public IndexingTaskRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repo = new IndexingTaskRepository(_context);
        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private IndexingTask CreateTask(
        TaskOperation op = TaskOperation.CloneRepository,
        IndexingTaskStatus status = IndexingTaskStatus.Pending,
        int priority = 0, Guid? chainId = null)
    {
        return new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id, Operation = op,
            Status = status, Priority = priority, ChainId = chainId,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task DequeueAsync_ReturnsPendingTask()
    {
        await _repo.AddAsync(CreateTask());
        await _repo.SaveChangesAsync();

        var result = await _repo.DequeueAsync();
        result.Should().NotBeNull();
        result!.Status.Should().Be(IndexingTaskStatus.Running);
        result.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DequeueAsync_NoPendingTasks_ReturnsNull()
    {
        await _repo.AddAsync(CreateTask(status: IndexingTaskStatus.Completed));
        await _repo.SaveChangesAsync();

        var result = await _repo.DequeueAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task DequeueAsync_ReturnsHighestPriorityFirst()
    {
        var lowPriority = CreateTask(op: TaskOperation.ScanCommit, priority: 1);
        var highPriority = CreateTask(op: TaskOperation.CloneRepository, priority: 10);
        await _repo.AddAsync(lowPriority);
        await _repo.AddAsync(highPriority);
        await _repo.SaveChangesAsync();

        var result = await _repo.DequeueAsync();
        result.Should().NotBeNull();
        result!.Operation.Should().Be(TaskOperation.CloneRepository);
        result.Priority.Should().Be(10);
    }

    [Fact]
    public async Task DequeueAsync_SamePriority_ReturnsFIFO()
    {
        var first = CreateTask(op: TaskOperation.ScanCommit);
        first.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        var second = CreateTask(op: TaskOperation.ExtractSnippets);
        second.CreatedAt = DateTime.UtcNow;

        await _repo.AddAsync(first);
        await _repo.AddAsync(second);
        await _repo.SaveChangesAsync();

        var result = await _repo.DequeueAsync();
        result!.Operation.Should().Be(TaskOperation.ScanCommit);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsStatusAndTimestamps()
    {
        var task = CreateTask();
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        await _repo.UpdateStatusAsync(task.Id, IndexingTaskStatus.Completed);

        var result = await _repo.GetByIdAsync(task.Id);
        result!.Status.Should().Be(IndexingTaskStatus.Completed);
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_Failed_StoresErrorMessage()
    {
        var task = CreateTask();
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        await _repo.UpdateStatusAsync(task.Id, IndexingTaskStatus.Failed, "Connection timeout");

        var result = await _repo.GetByIdAsync(task.Id);
        result!.Status.Should().Be(IndexingTaskStatus.Failed);
        result.ErrorMessage.Should().Be("Connection timeout");
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_NonExistentId_Throws()
    {
        var act = () => _repo.UpdateStatusAsync(Guid.NewGuid(), IndexingTaskStatus.Completed);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateProgressAsync_SetsProgress()
    {
        var task = CreateTask();
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        await _repo.UpdateProgressAsync(task.Id, 75);

        var result = await _repo.GetByIdAsync(task.Id);
        result!.Progress.Should().Be(75);
    }

    [Fact]
    public async Task GetByStatusAsync_ReturnsMatchingTasks()
    {
        await _repo.AddAsync(CreateTask(status: IndexingTaskStatus.Pending));
        await _repo.AddAsync(CreateTask(status: IndexingTaskStatus.Running));
        await _repo.AddAsync(CreateTask(status: IndexingTaskStatus.Pending));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByStatusAsync(IndexingTaskStatus.Pending);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByChainIdAsync_ReturnsChainTasks()
    {
        var chainId = Guid.NewGuid();
        await _repo.AddAsync(CreateTask(chainId: chainId));
        await _repo.AddAsync(CreateTask(chainId: chainId));
        await _repo.AddAsync(CreateTask(chainId: Guid.NewGuid()));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByChainIdAsync(chainId);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task CancelChainAsync_CancelsPendingTasksOnly()
    {
        var chainId = Guid.NewGuid();
        var pending1 = CreateTask(op: TaskOperation.ScanCommit, chainId: chainId);
        var pending2 = CreateTask(op: TaskOperation.ExtractSnippets, chainId: chainId);
        var running = CreateTask(op: TaskOperation.CloneRepository, status: IndexingTaskStatus.Running, chainId: chainId);

        await _repo.AddAsync(pending1);
        await _repo.AddAsync(pending2);
        await _repo.AddAsync(running);
        await _repo.SaveChangesAsync();

        await _repo.CancelChainAsync(chainId);

        var p1 = await _repo.GetByIdAsync(pending1.Id);
        var p2 = await _repo.GetByIdAsync(pending2.Id);
        var r = await _repo.GetByIdAsync(running.Id);

        p1!.Status.Should().Be(IndexingTaskStatus.Cancelled);
        p2!.Status.Should().Be(IndexingTaskStatus.Cancelled);
        r!.Status.Should().Be(IndexingTaskStatus.Running);
    }

    [Fact]
    public async Task GetByRepositoryAsync_ReturnsRepoTasks()
    {
        await _repo.AddAsync(CreateTask());
        await _repo.AddAsync(CreateTask(op: TaskOperation.SyncRepository));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByRepositoryAsync(_testRepo.Id);
        result.Should().HaveCount(2);
    }
}
