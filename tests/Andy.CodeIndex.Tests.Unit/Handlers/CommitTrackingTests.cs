using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class ExtractSnippetsCommitTrackingTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<IChunkingService> _chunkingServiceMock = new();
    private readonly Mock<IFileFilterService> _fileFilterServiceMock = new();
    private readonly ExtractSnippetsHandler _handler;
    private readonly Repository _testRepo;
    private readonly Commit _testCommit;

    public ExtractSnippetsCommitTrackingTests()
    {
        _context = TestDbContextFactory.Create();
        _fileFilterServiceMock.Setup(f => f.ShouldSkip(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<Repository?>()))
            .Returns((false, (string?)null));

        _handler = new ExtractSnippetsHandler(
            _context,
            _gitServiceMock.Object,
            _chunkingServiceMock.Object,
            _fileFilterServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<ExtractSnippetsHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);

        _testCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "abc123",
            Message = "test commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(_testCommit);
        _context.SaveChanges();

        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task HandleAsync_SetsCommitId_OnNewEnrichments()
    {
        SetupGitFiles([("Program.cs", "csharp", "public class Program { }", "blobsha1")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("public class Program { }", "Program.cs", null))
            .Returns([new CodeChunk { Content = "public class Program { }", StartLine = 1, EndLine = 1, FilePath = "Program.cs" }]);

        await RunHandler();

        var enrichment = _context.Enrichments.First(e => e.RepositoryId == _testRepo.Id);
        enrichment.CommitId.Should().Be(_testCommit.Id);
    }

    [Fact]
    public async Task HandleAsync_SkipsUnchangedFiles_WhenBlobSHAMatches()
    {
        // Set up a previous commit with file records
        var previousCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "prev123",
            Message = "previous commit",
            CommittedAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(previousCommit);

        // Previous commit had file A.cs with blob SHA "samehash"
        _context.RepositoryFiles.Add(new RepositoryFile
        {
            Id = Guid.NewGuid(),
            CommitId = previousCommit.Id,
            Path = "A.cs",
            Language = "csharp",
            Size = 50,
            Hash = "samehash",
            CreatedAt = DateTime.UtcNow
        });

        // Add existing enrichment for A.cs from before
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = "class A { }",
            FilePath = "A.cs",
            StartLine = 1,
            EndLine = 1,
            Language = "csharp",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        // Current commit has same file with same blob SHA
        SetupGitFiles([("A.cs", "csharp", "class A { }", "samehash")]);

        await RunHandler();

        // File should be skipped (not re-read or re-chunked)
        _gitServiceMock.Verify(
            g => g.ReadFileAsync("/tmp/test/repos/x", "abc123", "A.cs", It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not read file content for unchanged blob SHA");

        // Existing enrichment should still be present
        _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .Should().Be(1);

        // FilesSkipped should be recorded in IndexingRun
        var run = _context.IndexingRuns.First(r => r.RepositoryId == _testRepo.Id);
        run.FilesSkipped.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ReProcessesChangedFiles_WhenBlobSHADiffers()
    {
        // Set up a previous commit with file records
        var previousCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "prev123",
            Message = "previous commit",
            CommittedAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(previousCommit);

        // Previous commit had file A.cs with blob SHA "oldhash"
        _context.RepositoryFiles.Add(new RepositoryFile
        {
            Id = Guid.NewGuid(),
            CommitId = previousCommit.Id,
            Path = "A.cs",
            Language = "csharp",
            Size = 50,
            Hash = "oldhash",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        // Current commit has same file with different blob SHA
        SetupGitFiles([("A.cs", "csharp", "class A { int x; }", "newhash")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { int x; }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { int x; }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);

        await RunHandler();

        // File SHOULD be read and re-chunked
        _gitServiceMock.Verify(
            g => g.ReadFileAsync("/tmp/test/repos/x", "abc123", "A.cs", It.IsAny<CancellationToken>()),
            Times.Once,
            "Should read file content when blob SHA differs");

        // Enrichment should be created with CommitId
        var enrichment = _context.Enrichments.First(e => e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.Chunk);
        enrichment.CommitId.Should().Be(_testCommit.Id);
        enrichment.Content.Should().Contain("int x");
    }

    [Fact]
    public async Task HandleAsync_SetsCommitId_OnUpdatedEnrichments()
    {
        // Create existing enrichment without CommitId
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = "class A { int x; }",
            FilePath = "A.cs",
            StartLine = 1,
            EndLine = 1,
            Language = "csharp",
            CommitId = null,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // Modified content
        SetupGitFiles([("A.cs", "csharp", "class A { int x; int y; }", "newhash")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { int x; int y; }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { int x; int y; }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);

        await RunHandler();

        var enrichment = _context.Enrichments.First(e => e.FilePath == "A.cs");
        enrichment.CommitId.Should().Be(_testCommit.Id, "CommitId should be set on updated enrichments");
    }

    private void SetupGitFiles(List<(string path, string language, string content, string hash)> files)
    {
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files.Select(f => new GitFileInfo { Path = f.path, Size = f.content.Length, Language = f.language, Hash = f.hash }).ToList());
        foreach (var (path, _, content, _) in files)
        {
            _gitServiceMock.Setup(g => g.ReadFileAsync("/tmp/test/repos/x", "abc123", path, It.IsAny<CancellationToken>()))
                .ReturnsAsync(content);
        }
    }

    private async Task RunHandler()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractSnippets, CreatedAt = DateTime.UtcNow
        };
        await _handler.HandleAsync(task);
    }
}

public class ScanCommitHandler_CreatesRepositoryFileRecordsTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<ICommitRepository> _commitRepoMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly ScanCommitHandler _handler;
    private readonly Repository _testRepo;

    public ScanCommitHandler_CreatesRepositoryFileRecordsTests()
    {
        _context = TestDbContextFactory.Create();
        _handler = new ScanCommitHandler(
            _repoRepoMock.Object,
            _commitRepoMock.Object,
            _gitServiceMock.Object,
            _context,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<ScanCommitHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task HandleAsync_CreatesRepositoryFileRecords_WithBlobSHA()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetCommitsAsync("/tmp/test/repos/x", 10000, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitCommitInfo { Sha = "abc123", Message = "First", AuthorName = "Test", CommittedAt = DateTime.UtcNow }
            ]);
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitFileInfo { Path = "Program.cs", Size = 100, Language = "csharp", Hash = "blob_sha_1" },
                new GitFileInfo { Path = "README.md", Size = 50, Language = "markdown", Hash = "blob_sha_2" }
            ]);
        _commitRepoMock.Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Commit, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var repoFiles = _context.RepositoryFiles.ToList();
        repoFiles.Should().HaveCount(2);
        repoFiles.Should().Contain(f => f.Path == "Program.cs" && f.Hash == "blob_sha_1" && f.Language == "csharp");
        repoFiles.Should().Contain(f => f.Path == "README.md" && f.Hash == "blob_sha_2" && f.Language == "markdown");
    }

    [Fact]
    public async Task HandleAsync_UpdatesLastIndexedCommitSha_AfterScanningNewCommits()
    {
        // Regression test for #214: ScanCommitHandler must update LastIndexedCommitSha
        // so downstream handlers know which commit to process and future syncs
        // only fetch newer commits.
        _context.Repositories.Add(_testRepo);
        await _context.SaveChangesAsync();

        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetCommitsAsync("/tmp/test/repos/x", 10000, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitCommitInfo { Sha = "latest_sha_456", Message = "Latest commit", AuthorName = "Dev", CommittedAt = DateTime.UtcNow },
                new GitCommitInfo { Sha = "older_sha_123", Message = "Older commit", AuthorName = "Dev", CommittedAt = DateTime.UtcNow.AddDays(-1) }
            ]);
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "latest_sha_456", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _commitRepoMock.Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Commit, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit, ChainId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        // LastIndexedCommitSha should be set to the first (latest) commit
        _testRepo.LastIndexedCommitSha.Should().Be("latest_sha_456",
            "ScanCommitHandler must update LastIndexedCommitSha so future syncs only fetch newer commits");

        // CommitId should be propagated to the task for chaining
        task.CommitId.Should().NotBeNull("CommitId should be set on the task for downstream chain steps");
    }

    [Fact]
    public async Task HandleAsync_NoNewCommits_DoesNotUpdateLastIndexedCommitSha()
    {
        _testRepo.LastIndexedCommitSha = "existing_sha";
        _context.Repositories.Add(_testRepo);
        await _context.SaveChangesAsync();

        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetCommitsAsync("/tmp/test/repos/x", 10000, "existing_sha", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]); // No new commits

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit, ChainId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _testRepo.LastIndexedCommitSha.Should().Be("existing_sha",
            "LastIndexedCommitSha should not change when there are no new commits");
        task.CommitId.Should().BeNull("CommitId should remain null when no new commits found");
    }
}

public class LlmEnrichmentHandler_CommitIdTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IApiKeyResolver> _resolverMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Repository _testRepo;
    private readonly Commit _testCommit;

    public LlmEnrichmentHandler_CommitIdTests()
    {
        _context = TestDbContextFactory.Create();

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);

        _testCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "abc123",
            Message = "test commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(_testCommit);

        // Add some chunk enrichments so the prompt has context
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = "sample code",
            FilePath = "Program.cs",
            StartLine = 1,
            EndLine = 10,
            Language = "csharp",
            CreatedAt = DateTime.UtcNow
        });

        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task HandleAsync_SkipsWhenNoApiKey()
    {
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((string?)null, "https://api.openai.com/v1", "gpt-4o-mini", "none"));

        var handler = new CreateArchitectureDocsHandler(
            _context, _resolverMock.Object,
            Options.Create(new EnrichmentLlmOptions()),
            _httpClientFactoryMock.Object,
            NullLogger<CreateArchitectureDocsHandler>.Instance);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateArchitectureDocs, CreatedAt = DateTime.UtcNow
        };

        await handler.HandleAsync(task);

        // No enrichments should be created (besides the chunk we seeded)
        _context.Enrichments.Count(e => e.Subtype == EnrichmentSubtype.Physical)
            .Should().Be(0);
    }
}

public class ExtractDependenciesHandler_CommitIdTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<IDependencyParserService> _parserMock = new();
    private readonly ExtractDependenciesHandler _handler;
    private readonly Repository _testRepo;
    private readonly Commit _testCommit;

    public ExtractDependenciesHandler_CommitIdTests()
    {
        _context = TestDbContextFactory.Create();
        _handler = new ExtractDependenciesHandler(
            _context, _gitServiceMock.Object, _parserMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<ExtractDependenciesHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);

        _testCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "abc123",
            Message = "test commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(_testCommit);
        _context.SaveChanges();

        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task HandleAsync_SetsCommitId_OnDependencyEnrichment()
    {
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitFileInfo { Path = "package.json", Size = 200, Hash = "hash1" }]);
        _parserMock.Setup(p => p.CanParse("package.json")).Returns(true);
        _gitServiceMock.Setup(g => g.ReadFileAsync("/tmp/test/repos/x", "abc123", "package.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"dependencies\": {\"express\": \"^4.0\"}}");
        _parserMock.Setup(p => p.Parse("package.json", It.IsAny<string>()))
            .Returns([new PackageDependency { Name = "express", Version = "^4.0", Source = "npm", SourceFile = "package.json", Scope = "runtime" }]);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDependencies, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichment = _context.Enrichments.FirstOrDefault(e =>
            e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.Dependencies);
        enrichment.Should().NotBeNull();
        enrichment!.CommitId.Should().Be(_testCommit.Id);
    }
}

public class ExtractCommitHistoryHandler_CommitIdTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly ExtractCommitHistoryHandler _handler;
    private readonly Repository _testRepo;
    private readonly Commit _testCommit;

    public ExtractCommitHistoryHandler_CommitIdTests()
    {
        _context = TestDbContextFactory.Create();
        _handler = new ExtractCommitHistoryHandler(
            _context, _gitServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<ExtractCommitHistoryHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);

        _testCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "abc123",
            Message = "test commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(_testCommit);
        _context.SaveChanges();

        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task HandleAsync_SetsCommitId_OnCommitHistoryEnrichment()
    {
        _gitServiceMock.Setup(g => g.GetCommitsAsync("/tmp/test/repos/x", 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitCommitInfo { Sha = "abc123", Message = "Test", AuthorName = "Dev", CommittedAt = DateTime.UtcNow }]);
        _gitServiceMock.Setup(g => g.GetTagsAsync("/tmp/test/repos/x", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractCommitHistory, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichment = _context.Enrichments.FirstOrDefault(e =>
            e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.CommitHistory);
        enrichment.Should().NotBeNull();
        enrichment!.CommitId.Should().Be(_testCommit.Id);
    }
}
