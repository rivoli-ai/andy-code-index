namespace Andy.CodeIndex.Application.DTOs;

public class DailyActivityDto
{
    public DateTime Date { get; set; }
    public int CommitCount { get; set; }
}

public class WeeklyActivityDto
{
    public DateTime WeekStart { get; set; }
    public int CommitCount { get; set; }
    public int AuthorCount { get; set; }
}

public class ActivityStatsDto
{
    public int TotalCommits { get; set; }
    public int UniqueAuthors { get; set; }
    public double AvgPerDay { get; set; }
    public int MaxCommitsInDay { get; set; }
    public string MostActiveDay { get; set; } = "";
    public int LongestInactiveStreak { get; set; }
    public DateTime? LastCommitDate { get; set; }
}

public class GitActivityHeatmapDto
{
    public List<DailyActivityDto> DailyData { get; set; } = [];
    public List<WeeklyActivityDto> WeeklyData { get; set; } = [];
    public ActivityStatsDto Stats { get; set; } = new();
}

public class SparklineDto
{
    public List<WeeklyActivityDto> WeeklyData { get; set; } = [];
}
