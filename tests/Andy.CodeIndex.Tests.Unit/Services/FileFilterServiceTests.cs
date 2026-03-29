using System.Text.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class FileFilterServiceTests
{
    private static FileFilterService CreateService(FileFilterOptions? options = null)
    {
        var opts = Options.Create(options ?? new FileFilterOptions());
        return new FileFilterService(opts);
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("library.dll")]
    [InlineData("archive.zip")]
    [InlineData("font.woff2")]
    [InlineData("compiled.pyc")]
    public void ShouldSkip_GlobalSkipExtensions_ReturnsTrue(string filePath)
    {
        var service = CreateService();

        var (skip, reason) = service.ShouldSkip(filePath, 100);

        skip.Should().BeTrue();
        reason.Should().Contain("skip list");
    }

    [Theory]
    [InlineData("src/main.cs")]
    [InlineData("README.md")]
    [InlineData("package.json")]
    [InlineData("app.ts")]
    public void ShouldSkip_AllowedExtensions_ReturnsFalse(string filePath)
    {
        var service = CreateService();

        var (skip, _) = service.ShouldSkip(filePath, 100);

        skip.Should().BeFalse();
    }

    [Theory]
    [InlineData("node_modules/express/index.js")]
    [InlineData(".git/config")]
    [InlineData("vendor/lib/thing.go")]
    [InlineData("bin/Debug/app.dll")]
    [InlineData("dist/bundle.js")]
    [InlineData("build/output.js")]
    [InlineData("app.min.js")]
    [InlineData("styles.min.css")]
    [InlineData("package-lock.json")]
    [InlineData("yarn.lock")]
    public void ShouldSkip_GlobalSkipPatterns_ReturnsTrue(string filePath)
    {
        var service = CreateService();

        var (skip, reason) = service.ShouldSkip(filePath, 100);

        skip.Should().BeTrue();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void ShouldSkip_MaxFileSize_SkipsLargeFiles()
    {
        var service = CreateService(new FileFilterOptions { MaxFileSizeBytes = 500 });

        var (skip, reason) = service.ShouldSkip("large-file.cs", 1000);

        skip.Should().BeTrue();
        reason.Should().Contain("exceeds max");
    }

    [Fact]
    public void ShouldSkip_MaxFileSize_AllowsSmallFiles()
    {
        var service = CreateService(new FileFilterOptions { MaxFileSizeBytes = 500 });

        var (skip, _) = service.ShouldSkip("small-file.cs", 100);

        skip.Should().BeFalse();
    }

    [Fact]
    public void ShouldSkip_MergesRepoOverrides_AdditionalSkipExtensions()
    {
        var service = CreateService();
        var repo = new Repository
        {
            Name = "test",
            Url = "https://github.com/test/test",
            FileFilterOverrides = JsonSerializer.Serialize(new FileFilterOverridesDto
            {
                AdditionalSkipExtensions = [".csv", ".log"]
            })
        };

        var (skip, reason) = service.ShouldSkip("data.csv", 100, repo);

        skip.Should().BeTrue();
        reason.Should().Contain(".csv");
    }

    [Fact]
    public void ShouldSkip_MergesRepoOverrides_AdditionalSkipPatterns()
    {
        var service = CreateService();
        var repo = new Repository
        {
            Name = "test",
            Url = "https://github.com/test/test",
            FileFilterOverrides = JsonSerializer.Serialize(new FileFilterOverridesDto
            {
                AdditionalSkipPatterns = ["data/**", "fixtures/**"]
            })
        };

        var (skip, reason) = service.ShouldSkip("data/file.txt", 100, repo);

        skip.Should().BeTrue();
        reason.Should().Contain("data/**");
    }

    [Fact]
    public void ShouldSkip_RemoveSkipExtensions_AllowsPreviouslySkipped()
    {
        var service = CreateService();
        var repo = new Repository
        {
            Name = "test",
            Url = "https://github.com/test/test",
            FileFilterOverrides = JsonSerializer.Serialize(new FileFilterOverridesDto
            {
                RemoveSkipExtensions = [".png"]
            })
        };

        // .png is in default skip list, but removed by override
        var (skip, _) = service.ShouldSkip("logo.png", 100, repo);

        skip.Should().BeFalse();
    }

    [Fact]
    public void ShouldSkip_RepoOverride_MaxFileSizeBytes()
    {
        var service = CreateService(new FileFilterOptions { MaxFileSizeBytes = 500 });
        var repo = new Repository
        {
            Name = "test",
            Url = "https://github.com/test/test",
            FileFilterOverrides = JsonSerializer.Serialize(new FileFilterOverridesDto
            {
                MaxFileSizeBytes = 2000
            })
        };

        // 1000 bytes exceeds global (500) but is within repo override (2000)
        var (skip, _) = service.ShouldSkip("file.cs", 1000, repo);

        skip.Should().BeFalse();
    }

    [Fact]
    public void ShouldSkip_InvalidJson_FallsBackToGlobalConfig()
    {
        var service = CreateService();
        var repo = new Repository
        {
            Name = "test",
            Url = "https://github.com/test/test",
            FileFilterOverrides = "not valid json {{"
        };

        // Should still filter .dll using global config
        var (skip, _) = service.ShouldSkip("lib.dll", 100, repo);

        skip.Should().BeTrue();
    }

    [Fact]
    public void ShouldSkip_NullRepo_UsesGlobalConfig()
    {
        var service = CreateService();

        var (skip, _) = service.ShouldSkip("lib.dll", 100, null);

        skip.Should().BeTrue();
    }

    [Fact]
    public void ShouldSkip_ExtensionCheck_IsCaseInsensitive()
    {
        var service = CreateService();

        var (skip, _) = service.ShouldSkip("image.PNG", 100);

        skip.Should().BeTrue();
    }
}
