using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Repositories;

/// <summary>
/// SM.2.9 — Heartbeat / TimedOut signal + Seq watermark unit tests.
/// Covers: heartbeat stamping on Dequeue/UpdateStatus/UpdateProgress,
/// Seq monotonicity, TimedOutStalledTasks watchdog, and UpdateHeartbeatAsync.
/// </summary>
public class IndexingTaskWatermarkAndTimeoutTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly IndexingTaskRepository _repo;
    private readonly Repository _testRepo;

    public IndexingTaskWatermarkAndTimeoutTests()
    {
        _context = TestDbContextFactory.Create();
        _repo = new IndexingTaskRepository(_context);
        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "sm-test", Url = "https://github.com/test/sm",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private IndexingTask CreateTask(IndexingTaskStatus status = IndexingTaskStatus.Pending)
    {
        return new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CloneRepository,
            Status = status,
            Priority = 5,
            Seq = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- Seq watermark ---

    [Fact]
    public async Task UpdateStatusAsync_IncrementsSeq()
    {
        var task = CreateTask();
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var before = task.Seq;
        await _repo.UpdateStatusAsync(task.Id, IndexingTaskStatus.Running);

        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Seq.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task UpdateProgressAsync_IncrementsSeq()
    {
        var task = CreateTask(IndexingTaskStatus.Running);
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var before = task.Seq;
        await _repo.UpdateProgressAsync(task.Id, 50, "halfway");

        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Seq.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Seq_IsMonotonicallyIncreasing_AcrossMultipleUpdates()
    {
        var task = CreateTask();
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        await _repo.UpdateStatusAsync(task.Id, IndexingTaskStatus.Running);
        var r1 = await _repo.GetByIdAsync(task.Id);
        var seq1 = r1!.Seq;

        await _repo.UpdateProgressAsync(task.Id, 25);
        var r2 = await _repo.GetByIdAsync(task.Id);
        var seq2 = r2!.Seq;

        await _repo.UpdateProgressAsync(task.Id, 75);
        var r3 = await _repo.GetByIdAsync(task.Id);
        var seq3 = r3!.Seq;

        await _repo.UpdateStatusAsync(task.Id, IndexingTaskStatus.Completed);
        var r4 = await _repo.GetByIdAsync(task.Id);
        var seq4 = r4!.Seq;

        seq1.Should().BeLessThan(seq2);
        seq2.Should().BeLessThan(seq3);
        seq3.Should().BeLessThan(seq4);
    }

    [Fact]
    public async Task DequeueAsync_SetsLastHeartbeatAtOnTransitionToRunning()
    {
        var task = CreateTask();
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var dequeued = await _repo.DequeueAsync();

        dequeued.Should().NotBeNull();
        dequeued!.LastHeartbeatAt.Should().NotBeNull();
        dequeued.LastHeartbeatAt.Should().BeAfter(before);
    }

    // --- Heartbeat ---

    [Fact]
    public async Task UpdateHeartbeatAsync_AdvancesSeqAndLastHeartbeatAt()
    {
        var task = CreateTask(IndexingTaskStatus.Running);
        task.StartedAt = DateTime.UtcNow;
        task.LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-1);
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var seqBefore = task.Seq;
        var heartbeatBefore = task.LastHeartbeatAt!.Value;

        await _repo.UpdateHeartbeatAsync(task.Id);

        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Seq.Should().BeGreaterThan(seqBefore);
        updated.LastHeartbeatAt.Should().BeAfter(heartbeatBefore);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_OnNonRunningTask_DoesNotThrow()
    {
        var task = CreateTask(IndexingTaskStatus.Completed);
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var act = () => _repo.UpdateHeartbeatAsync(task.Id);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_OnNonExistentTask_DoesNotThrow()
    {
        var act = () => _repo.UpdateHeartbeatAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    // --- TimedOut backstop ---

    [Fact]
    public async Task TimeOutStalledTasksAsync_FlipsOldRunningTaskToTimedOut()
    {
        var task = CreateTask(IndexingTaskStatus.Running);
        task.StartedAt = DateTime.UtcNow.AddMinutes(-60);
        task.LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-60);
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();
        var seqBefore = task.Seq; // capture before watchdog modifies it

        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var timedOut = await _repo.TimeOutStalledTasksAsync(cutoff);

        timedOut.Should().Contain(task.Id);
        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(IndexingTaskStatus.TimedOut);
        updated.CompletedAt.Should().NotBeNull();
        updated.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        updated.Seq.Should().BeGreaterThan(seqBefore);
    }

    [Fact]
    public async Task TimeOutStalledTasksAsync_DoesNotFlipRecentTask()
    {
        var task = CreateTask(IndexingTaskStatus.Running);
        task.StartedAt = DateTime.UtcNow;
        task.LastHeartbeatAt = DateTime.UtcNow;
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var timedOut = await _repo.TimeOutStalledTasksAsync(cutoff);

        timedOut.Should().NotContain(task.Id);
        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(IndexingTaskStatus.Running);
    }

    [Fact]
    public async Task TimeOutStalledTasksAsync_DoesNotAffectNonRunningTasks()
    {
        var completed = CreateTask(IndexingTaskStatus.Completed);
        var failed = CreateTask(IndexingTaskStatus.Failed);
        var pending = CreateTask(IndexingTaskStatus.Pending);
        await _repo.AddAsync(completed);
        await _repo.AddAsync(failed);
        await _repo.AddAsync(pending);
        await _repo.SaveChangesAsync();

        // All have null LastHeartbeatAt but are not Running
        var cutoff = DateTime.UtcNow.AddMinutes(10); // future cutoff catches everything
        var timedOut = await _repo.TimeOutStalledTasksAsync(cutoff);

        timedOut.Should().NotContain(completed.Id);
        timedOut.Should().NotContain(failed.Id);
        timedOut.Should().NotContain(pending.Id);
    }

    [Fact]
    public async Task TimeOutStalledTasksAsync_FlipsRunningTaskWithNullHeartbeat()
    {
        // A Running task with null LastHeartbeatAt (e.g. created before this feature)
        // should also be caught by the cutoff when cutoff > StartedAt
        var task = CreateTask(IndexingTaskStatus.Running);
        task.StartedAt = DateTime.UtcNow.AddMinutes(-60);
        task.LastHeartbeatAt = null;
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var timedOut = await _repo.TimeOutStalledTasksAsync(cutoff);

        timedOut.Should().Contain(task.Id);
        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(IndexingTaskStatus.TimedOut);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToTimedOut_SetsCompletedAt()
    {
        var task = CreateTask(IndexingTaskStatus.Running);
        await _repo.AddAsync(task);
        await _repo.SaveChangesAsync();

        await _repo.UpdateStatusAsync(task.Id, IndexingTaskStatus.TimedOut, "Watchdog triggered");

        var updated = await _repo.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(IndexingTaskStatus.TimedOut);
        updated.CompletedAt.Should().NotBeNull();
    }
}
