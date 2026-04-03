using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class GitServiceTests
{
    [Theory]
    [InlineData("src/Program.cs", "*.cs", false)]  // * doesn't cross /
    [InlineData("Program.cs", "*.cs", true)]
    [InlineData("src/Program.cs", "*.ts", false)]
    [InlineData("src/Controllers/HomeController.cs", "src/**/*.cs", true)]
    [InlineData("src/Controllers/HomeController.cs", "tests/**/*.cs", false)]
    [InlineData("docs/README.md", "**/*.md", true)]
    [InlineData("file.txt", "*.*", true)]
    [InlineData("src/deep/nested/file.py", "**/*.py", true)]
    [InlineData("test.js", "*.js", true)]
    [InlineData("src/test.js", "*.js", false)] // * doesn't match /
    public void MatchGlob_MatchesCorrectly(string path, string pattern, bool expected)
    {
        GitService.MatchGlob(path, pattern).Should().Be(expected);
    }

    [Fact]
    public void GetCloneDir_ReturnsCorrectPath()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<GitService>();
        var service = new GitService(logger);
        var repoId = Guid.Parse("12345678-1234-1234-1234-123456789012");

        var result = service.GetCloneDir("/data", repoId);
        result.Should().Be(Path.Combine("/data", "repos", "12345678-1234-1234-1234-123456789012"));
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("HEAD", true)]
    [InlineData("abc123def456", true)]
    [InlineData("v1.0.0", true)]
    [InlineData("feature/my-branch", true)]
    [InlineData("HEAD~1", true)]
    [InlineData("HEAD^{tree}", true)]
    [InlineData("refs/heads/main", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidRef_ValidatesCorrectly(string gitRef, bool expected)
    {
        GitService.IsValidRef(gitRef).Should().Be(expected);
    }

    // --- ParseCommitLog tests ---

    [Fact]
    public void ParseCommitLog_ParsesParentShas_SingleParent()
    {
        var output = "abc123\nparent1\nInitial commit\nAuthor\nauthor@test.com\n2024-01-15T10:30:00+00:00\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(1);
        commits[0].Sha.Should().Be("abc123");
        commits[0].ParentShas.Should().ContainSingle().Which.Should().Be("parent1");
    }

    [Fact]
    public void ParseCommitLog_ParsesParentShas_MultipleParents()
    {
        var output = "abc123\nparent1 parent2\nMerge commit\nAuthor\nauthor@test.com\n2024-01-15T10:30:00+00:00\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(1);
        commits[0].ParentShas.Should().HaveCount(2);
        commits[0].ParentShas.Should().Contain("parent1");
        commits[0].ParentShas.Should().Contain("parent2");
    }

    [Fact]
    public void ParseCommitLog_ParsesParentShas_NoParent_RootCommit()
    {
        var output = "abc123\n\nRoot commit\nAuthor\nauthor@test.com\n2024-01-15T10:30:00+00:00\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(1);
        commits[0].ParentShas.Should().BeEmpty();
    }

    [Fact]
    public void ParseCommitLog_HandlesMultipleCommits()
    {
        var output =
            "sha1\nparent_a\nFirst commit\nAlice\nalice@test.com\n2024-01-15T10:30:00+00:00\n---\n" +
            "sha2\nsha1\nSecond commit\nBob\nbob@test.com\n2024-01-16T10:30:00+00:00\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(2);
        commits[0].Sha.Should().Be("sha1");
        commits[0].Message.Should().Be("First commit");
        commits[0].AuthorName.Should().Be("Alice");
        commits[1].Sha.Should().Be("sha2");
        commits[1].ParentShas.Should().ContainSingle().Which.Should().Be("sha1");
    }

    [Fact]
    public void ParseCommitLog_HandlesEmptyOutput()
    {
        var commits = GitService.ParseCommitLog("");
        commits.Should().BeEmpty();
    }

    [Fact]
    public void ParseCommitLog_SkipsMalformedEntries()
    {
        var output = "short\ntoo few lines\n---\n" +
                     "abc123\nparent1\nValid commit\nAuthor\nauthor@test.com\n2024-01-15T10:30:00+00:00\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(1);
        commits[0].Sha.Should().Be("abc123");
    }

    [Fact]
    public void ParseCommitLog_HandlesInvalidDate()
    {
        var output = "abc123\nparent1\nCommit\nAuthor\nauthor@test.com\nnot-a-date\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().BeEmpty();
    }

    [Fact]
    public void ParseCommitLog_ConvertsDateToUtc()
    {
        var output = "abc123\nparent1\nCommit\nAuthor\nauthor@test.com\n2024-01-15T10:30:00+05:00\n---\n";
        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(1);
        commits[0].CommittedAt.Kind.Should().Be(DateTimeKind.Utc);
        commits[0].CommittedAt.Hour.Should().Be(5); // 10:30 +05:00 = 05:30 UTC
    }

    [Fact]
    public void ParseCommitLog_LargeHistory_ParsesAllCommits()
    {
        // Simulate a repo with 500 commits spanning 12 months.
        // This validates that ParseCommitLog handles large volumes and
        // preserves the original commit dates (regression: limit=100 caused
        // only the most recent commits to be imported, making heatmaps empty).
        var sb = new System.Text.StringBuilder();
        var baseDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 500; i++)
        {
            var date = baseDate.AddDays(i * 0.7); // spread across ~350 days
            var sha = $"sha{i:D6}";
            var parent = i > 0 ? $"sha{i - 1:D6}" : "";
            sb.Append($"{sha}\n{parent}\nCommit #{i}\nDev\ndev@test.com\n{date:yyyy-MM-ddTHH:mm:ss+00:00}\n---\n");
        }

        var commits = GitService.ParseCommitLog(sb.ToString());

        commits.Should().HaveCount(500);

        // Verify dates span the full range, not clustered in a single day
        var distinctDates = commits.Select(c => c.CommittedAt.Date).Distinct().ToList();
        distinctDates.Count.Should().BeGreaterThan(300, "commits should span many distinct days, not be clustered");

        // Verify earliest and latest dates are months apart
        var earliest = commits.Min(c => c.CommittedAt);
        var latest = commits.Max(c => c.CommittedAt);
        (latest - earliest).TotalDays.Should().BeGreaterThan(300, "history should span nearly a year");
    }

    [Fact]
    public void ParseCommitLog_PreservesOriginalDates_NotSyncTime()
    {
        // Regression test: commits must retain their original git author dates,
        // NOT the timestamp when the sync happened.
        var oldDate = "2024-06-15T14:30:00+00:00";
        var recentDate = "2025-03-20T09:00:00+00:00";
        var output =
            $"sha001\n\nOld commit\nAlice\nalice@test.com\n{oldDate}\n---\n" +
            $"sha002\nsha001\nRecent commit\nBob\nbob@test.com\n{recentDate}\n---\n";

        var commits = GitService.ParseCommitLog(output);

        commits.Should().HaveCount(2);
        commits[0].CommittedAt.Should().Be(new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc));
        commits[1].CommittedAt.Should().Be(new DateTime(2025, 3, 20, 9, 0, 0, DateTimeKind.Utc));

        // Dates should be months apart, proving we're using git dates not sync time
        (commits[1].CommittedAt - commits[0].CommittedAt).TotalDays.Should().BeGreaterThan(200);
    }

    [Fact]
    public void GetCommitsAsync_DefaultLimit_IsHighEnoughForFullHistory()
    {
        // Verify the default limit parameter is >= 5000 to prevent
        // truncating commit history on initial import.
        // This was the root cause of empty heatmaps: limit=100 meant only
        // the most recent 100 commits were imported on first sync.
        var method = typeof(GitService).GetMethod("GetCommitsAsync",
            new[] { typeof(string), typeof(int), typeof(string), typeof(CancellationToken) });

        method.Should().NotBeNull("GetCommitsAsync should exist");

        var limitParam = method!.GetParameters().First(p => p.Name == "limit");
        limitParam.HasDefaultValue.Should().BeTrue();

        var defaultLimit = (int)limitParam.DefaultValue!;
        defaultLimit.Should().BeGreaterOrEqualTo(5000,
            "default limit must be high enough to capture full repo history on initial import");
    }
}
