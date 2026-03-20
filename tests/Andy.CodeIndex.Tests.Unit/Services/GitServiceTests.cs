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
}
