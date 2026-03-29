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
}
