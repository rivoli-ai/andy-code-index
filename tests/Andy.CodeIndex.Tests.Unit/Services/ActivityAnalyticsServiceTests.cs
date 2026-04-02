using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class ActivityAnalyticsServiceTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly ActivityAnalyticsService _service;
    private readonly Guid _repoId = Guid.NewGuid();

    public ActivityAnalyticsServiceTests()
    {
        _context = TestDbContextFactory.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        _service = new ActivityAnalyticsService(_context, cache);

        // Create the repository
        _context.Repositories.Add(new Repository
        {
            Id = _repoId,
            Name = "test-repo",
            Url = "https://github.com/test/repo",
            Provider = GitProvider.GitHub,
            Status = "indexed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private void SeedCommit(DateTime committedAt, string authorEmail = "dev@test.com")
    {
        _context.Commits.Add(new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _repoId,
            Sha = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
            Message = "test commit",
            AuthorEmail = authorEmail,
            AuthorName = "Dev",
            CommittedAt = committedAt,
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    private void SeedCommits(Guid repoId, DateTime committedAt, string authorEmail = "dev@test.com")
    {
        _context.Commits.Add(new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = repoId,
            Sha = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
            Message = "test commit",
            AuthorEmail = authorEmail,
            AuthorName = "Dev",
            CommittedAt = committedAt,
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    [Fact]
    public async Task EmptyRepo_ReturnsEmptyData()
    {
        var result = await _service.GetHeatmapAsync(_repoId);

        result.Stats.TotalCommits.Should().Be(0);
        result.Stats.UniqueAuthors.Should().Be(0);
        result.Stats.AvgPerDay.Should().Be(0);
        result.Stats.MaxCommitsInDay.Should().Be(0);
        result.Stats.MostActiveDay.Should().BeEmpty();
        result.Stats.LastCommitDate.Should().BeNull();
        result.DailyData.Should().NotBeEmpty(); // Should have zero-filled days
        result.WeeklyData.Should().NotBeEmpty(); // Should have zero-filled weeks
    }

    [Fact]
    public async Task SingleCommit_ReturnsSingleDay()
    {
        var commitDate = DateTime.UtcNow.Date.AddDays(-5);
        SeedCommit(commitDate);

        var result = await _service.GetHeatmapAsync(_repoId);

        result.Stats.TotalCommits.Should().Be(1);
        result.Stats.UniqueAuthors.Should().Be(1);
        result.Stats.MaxCommitsInDay.Should().Be(1);
        result.DailyData.Should().Contain(d => d.Date == commitDate && d.CommitCount == 1);
    }

    [Fact]
    public async Task FullYear_CorrectDayCount()
    {
        // Seed one commit so we have data, but check total day count
        SeedCommit(DateTime.UtcNow.Date.AddDays(-100));

        var result = await _service.GetHeatmapAsync(_repoId, weeksBack: 52);

        // Should have approximately 365 days (52 * 7 + 1 for today)
        result.DailyData.Count.Should().BeGreaterOrEqualTo(364);
        result.DailyData.Count.Should().BeLessOrEqualTo(366);
    }

    [Fact]
    public async Task WeeklyRollup_SundayStart()
    {
        // Seed commits on a known Wednesday
        var wednesday = DateTime.UtcNow.Date;
        while (wednesday.DayOfWeek != DayOfWeek.Wednesday) wednesday = wednesday.AddDays(-1);

        SeedCommit(wednesday);
        SeedCommit(wednesday);

        var result = await _service.GetHeatmapAsync(_repoId);

        var expectedSundayStart = ActivityAnalyticsService.GetSundayWeekStart(wednesday);
        result.WeeklyData.Should().Contain(w => w.WeekStart == expectedSundayStart && w.CommitCount == 2);
    }

    [Fact]
    public async Task Stats_AvgPerDay()
    {
        var today = DateTime.UtcNow.Date;
        // 10 commits spread over a few days
        for (int i = 0; i < 10; i++)
        {
            SeedCommit(today.AddDays(-i % 5));
        }

        var result = await _service.GetHeatmapAsync(_repoId, weeksBack: 1);

        result.Stats.TotalCommits.Should().Be(10);
        result.Stats.AvgPerDay.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Stats_MostActiveDay()
    {
        var today = DateTime.UtcNow.Date;
        // Find next Monday
        var monday = today;
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);

        // Seed 5 commits on Monday
        for (int i = 0; i < 5; i++)
        {
            SeedCommit(monday);
        }
        // Seed 1 commit on Tuesday
        SeedCommit(monday.AddDays(1));

        var result = await _service.GetHeatmapAsync(_repoId, weeksBack: 4);

        result.Stats.MostActiveDay.Should().Be("Monday");
    }

    [Fact]
    public async Task Stats_LongestInactiveStreak()
    {
        var today = DateTime.UtcNow.Date;
        // Commit today and 10 days ago, leaving 9 inactive days in between
        SeedCommit(today);
        SeedCommit(today.AddDays(-10));

        var result = await _service.GetHeatmapAsync(_repoId, weeksBack: 4);

        result.Stats.LongestInactiveStreak.Should().BeGreaterOrEqualTo(9);
    }

    [Fact]
    public async Task BulkSparklines_MultipleRepos()
    {
        var repo2Id = Guid.NewGuid();
        _context.Repositories.Add(new Repository
        {
            Id = repo2Id,
            Name = "test-repo-2",
            Url = "https://github.com/test/repo2",
            Provider = GitProvider.GitHub,
            Status = "indexed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();

        SeedCommit(DateTime.UtcNow.Date.AddDays(-3));
        SeedCommits(repo2Id, DateTime.UtcNow.Date.AddDays(-1));

        var result = await _service.GetBulkSparklinesAsync([_repoId, repo2Id]);

        result.Should().ContainKey(_repoId);
        result.Should().ContainKey(repo2Id);
        result[_repoId].WeeklyData.Should().NotBeEmpty();
        result[repo2Id].WeeklyData.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BulkSparklines_EmptyList()
    {
        var result = await _service.GetBulkSparklinesAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SparklineReturns52Weeks()
    {
        SeedCommit(DateTime.UtcNow.Date.AddDays(-100));

        var result = await _service.GetSparklineAsync(_repoId, weeksBack: 52);

        result.WeeklyData.Should().HaveCount(52);
        // Most weeks should have 0 commits
        result.WeeklyData.Count(w => w.CommitCount == 0).Should().BeGreaterThan(40);
        // One week should have 1 commit
        result.WeeklyData.Count(w => w.CommitCount == 1).Should().Be(1);
    }

    [Fact]
    public void GetSundayWeekStart_ReturnsSunday()
    {
        // Wednesday 2024-01-10 -> Sunday 2024-01-07
        var wednesday = new DateTime(2024, 1, 10);
        var result = ActivityAnalyticsService.GetSundayWeekStart(wednesday);
        result.Should().Be(new DateTime(2024, 1, 7));
        result.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void GetSundayWeekStart_SundayReturnsItself()
    {
        var sunday = new DateTime(2024, 1, 7);
        var result = ActivityAnalyticsService.GetSundayWeekStart(sunday);
        result.Should().Be(sunday);
    }

    [Fact]
    public void ComputeLongestInactiveStreak_AllZeros()
    {
        var data = Enumerable.Range(0, 10)
            .Select(i => new DailyActivityDto { Date = DateTime.UtcNow.Date.AddDays(-i), CommitCount = 0 })
            .ToList();

        var result = ActivityAnalyticsService.ComputeLongestInactiveStreak(data);
        result.Should().Be(10);
    }

    [Fact]
    public void ComputeLongestInactiveStreak_NoZeros()
    {
        var data = Enumerable.Range(0, 5)
            .Select(i => new DailyActivityDto { Date = DateTime.UtcNow.Date.AddDays(-i), CommitCount = 1 })
            .ToList();

        var result = ActivityAnalyticsService.ComputeLongestInactiveStreak(data);
        result.Should().Be(0);
    }

    [Fact]
    public async Task MultipleAuthors_UniqueAuthorsCount()
    {
        var today = DateTime.UtcNow.Date;
        SeedCommit(today, "alice@test.com");
        SeedCommit(today, "bob@test.com");
        SeedCommit(today, "alice@test.com");

        var result = await _service.GetHeatmapAsync(_repoId, weeksBack: 1);

        result.Stats.TotalCommits.Should().Be(3);
        result.Stats.UniqueAuthors.Should().Be(2);
    }
}
