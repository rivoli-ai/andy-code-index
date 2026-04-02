using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ActivityAnalyticsService : IActivityAnalyticsService
{
    private readonly CodeIndexDbContext _context;
    private readonly IMemoryCache _cache;

    public ActivityAnalyticsService(CodeIndexDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<GitActivityHeatmapDto> GetHeatmapAsync(Guid repositoryId, int weeksBack = 52, CancellationToken ct = default)
    {
        var cacheKey = $"activity-heatmap:{repositoryId}:{weeksBack}";
        if (_cache.TryGetValue(cacheKey, out GitActivityHeatmapDto? cached) && cached is not null)
            return cached;

        var cutoff = DateTime.UtcNow.Date.AddDays(-weeksBack * 7);
        var today = DateTime.UtcNow.Date;

        var commits = await _context.Commits
            .Where(c => c.RepositoryId == repositoryId && c.CommittedAt >= cutoff)
            .Select(c => new { c.CommittedAt, c.AuthorEmail })
            .ToListAsync(ct);

        // Daily aggregation
        var dailyGroups = commits
            .GroupBy(c => c.CommittedAt.Date)
            .ToDictionary(g => g.Key, g => new { Count = g.Count(), Authors = g.Select(x => x.AuthorEmail).Distinct().Count() });

        // Fill in zero-count days
        var dailyData = new List<DailyActivityDto>();
        for (var date = cutoff; date <= today; date = date.AddDays(1))
        {
            dailyData.Add(new DailyActivityDto
            {
                Date = date,
                CommitCount = dailyGroups.ContainsKey(date) ? dailyGroups[date].Count : 0
            });
        }

        // Weekly aggregation
        var weeklyData = BuildWeeklyData(commits.Select(c => new CommitInfo(c.CommittedAt, c.AuthorEmail)).ToList(), weeksBack);

        // Stats
        var stats = ComputeStats(commits.Select(c => new CommitInfo(c.CommittedAt, c.AuthorEmail)).ToList(), dailyData, weeksBack);

        var result = new GitActivityHeatmapDto
        {
            DailyData = dailyData,
            WeeklyData = weeklyData,
            Stats = stats
        };

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(60));
        return result;
    }

    public async Task<SparklineDto> GetSparklineAsync(Guid repositoryId, int weeksBack = 52, CancellationToken ct = default)
    {
        var cacheKey = $"activity-sparkline:{repositoryId}:{weeksBack}";
        if (_cache.TryGetValue(cacheKey, out SparklineDto? cached) && cached is not null)
            return cached;

        var cutoff = DateTime.UtcNow.Date.AddDays(-weeksBack * 7);

        var commits = await _context.Commits
            .Where(c => c.RepositoryId == repositoryId && c.CommittedAt >= cutoff)
            .Select(c => new { c.CommittedAt, c.AuthorEmail })
            .ToListAsync(ct);

        var weeklyData = BuildWeeklyData(commits.Select(c => new CommitInfo(c.CommittedAt, c.AuthorEmail)).ToList(), weeksBack);

        var result = new SparklineDto { WeeklyData = weeklyData };

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(60));
        return result;
    }

    public async Task<Dictionary<Guid, SparklineDto>> GetBulkSparklinesAsync(IEnumerable<Guid> repositoryIds, int weeksBack = 52, CancellationToken ct = default)
    {
        var ids = repositoryIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, SparklineDto>();

        // Check cache for already-computed sparklines
        var result = new Dictionary<Guid, SparklineDto>();
        var uncachedIds = new List<Guid>();

        foreach (var id in ids)
        {
            var cacheKey = $"activity-sparkline:{id}:{weeksBack}";
            if (_cache.TryGetValue(cacheKey, out SparklineDto? cached) && cached is not null)
                result[id] = cached;
            else
                uncachedIds.Add(id);
        }

        if (uncachedIds.Count == 0)
            return result;

        var cutoff = DateTime.UtcNow.Date.AddDays(-weeksBack * 7);

        var commits = await _context.Commits
            .Where(c => uncachedIds.Contains(c.RepositoryId) && c.CommittedAt >= cutoff)
            .Select(c => new { c.RepositoryId, c.CommittedAt, c.AuthorEmail })
            .ToListAsync(ct);

        var groupedByRepo = commits.GroupBy(c => c.RepositoryId);

        foreach (var repoGroup in groupedByRepo)
        {
            var weeklyData = BuildWeeklyData(
                repoGroup.Select(c => new CommitInfo(c.CommittedAt, c.AuthorEmail)).ToList(),
                weeksBack);

            var sparkline = new SparklineDto { WeeklyData = weeklyData };
            result[repoGroup.Key] = sparkline;

            var cacheKey = $"activity-sparkline:{repoGroup.Key}:{weeksBack}";
            _cache.Set(cacheKey, sparkline, TimeSpan.FromMinutes(60));
        }

        // Add empty sparklines for repos with no commits
        foreach (var id in uncachedIds)
        {
            if (!result.ContainsKey(id))
            {
                var sparkline = new SparklineDto { WeeklyData = BuildWeeklyData([], weeksBack) };
                result[id] = sparkline;

                var cacheKey = $"activity-sparkline:{id}:{weeksBack}";
                _cache.Set(cacheKey, sparkline, TimeSpan.FromMinutes(60));
            }
        }

        return result;
    }

    internal static DateTime GetSundayWeekStart(DateTime date) => date.AddDays(-(int)date.DayOfWeek).Date;

    internal static List<WeeklyActivityDto> BuildWeeklyData(List<CommitInfo> commits, int weeksBack)
    {
        var today = DateTime.UtcNow.Date;
        var currentWeekStart = GetSundayWeekStart(today);
        var startWeek = currentWeekStart.AddDays(-((weeksBack - 1) * 7));

        var weeklyGroups = commits
            .GroupBy(c => GetSundayWeekStart(c.CommittedAt))
            .ToDictionary(
                g => g.Key,
                g => new { Count = g.Count(), Authors = g.Select(x => x.AuthorEmail).Distinct().Count() });

        var weeklyData = new List<WeeklyActivityDto>();
        for (var week = startWeek; week <= currentWeekStart; week = week.AddDays(7))
        {
            weeklyData.Add(new WeeklyActivityDto
            {
                WeekStart = week,
                CommitCount = weeklyGroups.ContainsKey(week) ? weeklyGroups[week].Count : 0,
                AuthorCount = weeklyGroups.ContainsKey(week) ? weeklyGroups[week].Authors : 0
            });
        }

        return weeklyData;
    }

    internal static ActivityStatsDto ComputeStats(List<CommitInfo> commits, List<DailyActivityDto> dailyData, int weeksBack)
    {
        if (commits.Count == 0)
        {
            return new ActivityStatsDto
            {
                TotalCommits = 0,
                UniqueAuthors = 0,
                AvgPerDay = 0,
                MaxCommitsInDay = 0,
                MostActiveDay = "",
                LongestInactiveStreak = dailyData.Count,
                LastCommitDate = null
            };
        }

        var totalDays = Math.Max(dailyData.Count, 1);
        var dayOfWeekCounts = commits
            .GroupBy(c => c.CommittedAt.DayOfWeek)
            .OrderByDescending(g => g.Count())
            .First();

        return new ActivityStatsDto
        {
            TotalCommits = commits.Count,
            UniqueAuthors = commits.Select(c => c.AuthorEmail).Distinct().Count(),
            AvgPerDay = Math.Round((double)commits.Count / totalDays, 2),
            MaxCommitsInDay = dailyData.Max(d => d.CommitCount),
            MostActiveDay = dayOfWeekCounts.Key.ToString(),
            LongestInactiveStreak = ComputeLongestInactiveStreak(dailyData),
            LastCommitDate = commits.Max(c => c.CommittedAt)
        };
    }

    internal static int ComputeLongestInactiveStreak(List<DailyActivityDto> dailyData)
    {
        var maxStreak = 0;
        var currentStreak = 0;

        foreach (var day in dailyData)
        {
            if (day.CommitCount == 0)
            {
                currentStreak++;
                maxStreak = Math.Max(maxStreak, currentStreak);
            }
            else
            {
                currentStreak = 0;
            }
        }

        return maxStreak;
    }

    internal record CommitInfo(DateTime CommittedAt, string? AuthorEmail);
}
