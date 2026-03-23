using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class CloneRepositoryHandlerTests : IDisposable
{
    private readonly Andy.CodeIndex.Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly CloneRepositoryHandler _handler;
    private readonly Repository _testRepo;

    public CloneRepositoryHandlerTests()
    {
        _context = Helpers.TestDbContextFactory.Create();
        _handler = new CloneRepositoryHandler(
            _context,
            _gitServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<CloneRepositoryHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsCloneRepository()
    {
        _handler.Operation.Should().Be(TaskOperation.CloneRepository);
    }

    [Fact]
    public async Task HandleAsync_ClonesAndUpdatesBranches()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.CloneAsync(_testRepo.Url, "/tmp/test/repos/x", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetBranchesAsync("/tmp/test/repos/x", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitBranchInfo { Name = "main", HeadCommitSha = "abc123", IsDefault = true },
                new GitBranchInfo { Name = "develop", HeadCommitSha = "def456", IsDefault = false }
            ]);
        _gitServiceMock.Setup(g => g.GetTagsAsync("/tmp/test/repos/x", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitTagInfo { Name = "v1.0", CommitSha = "abc123" }]);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CloneRepository, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.Status.Should().Be("cloned");
        repo.DefaultBranch.Should().Be("main");
        repo.LastSyncedAt.Should().NotBeNull();

        var branches = _context.Branches.Where(b => b.RepositoryId == _testRepo.Id).ToList();
        branches.Should().HaveCount(2);
        var tags = _context.Tags.Where(t => t.RepositoryId == _testRepo.Id).ToList();
        tags.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_CloneFails_SetsStatusToError()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.CloneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("git clone failed: auth required"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CloneRepository, CreatedAt = DateTime.UtcNow
        };

        var act = () => _handler.HandleAsync(task);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*auth required*");

        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.Status.Should().Be("error");
    }

    [Fact]
    public async Task HandleAsync_BranchFetchFails_SetsStatusToError()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.CloneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetBranchesAsync("/tmp/test/repos/x", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("git branch failed"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CloneRepository, CreatedAt = DateTime.UtcNow
        };

        var act = () => _handler.HandleAsync(task);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.Status.Should().Be("error");
    }

    [Fact]
    public async Task HandleAsync_NonExistentRepo_Throws()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = Guid.NewGuid(), // Not in DB
            Operation = TaskOperation.CloneRepository, CreatedAt = DateTime.UtcNow
        };

        var act = () => _handler.HandleAsync(task);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }
}

public class ScanCommitHandlerTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<ICommitRepository> _commitRepoMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly ScanCommitHandler _handler;
    private readonly Repository _testRepo;

    public ScanCommitHandlerTests()
    {
        _handler = new ScanCommitHandler(
            _repoRepoMock.Object,
            _commitRepoMock.Object,
            _gitServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<ScanCommitHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public void Operation_IsScanCommit()
    {
        _handler.Operation.Should().Be(TaskOperation.ScanCommit);
    }

    [Fact]
    public async Task HandleAsync_StoresNewCommits()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetCommitsAsync("/tmp/test/repos/x", 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitCommitInfo { Sha = "abc123", Message = "First", AuthorName = "Test", CommittedAt = DateTime.UtcNow },
                new GitCommitInfo { Sha = "def456", Message = "Second", AuthorName = "Test", CommittedAt = DateTime.UtcNow }
            ]);
        _commitRepoMock.Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Commit, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _commitRepoMock.Verify(r => r.AddAsync(It.IsAny<Commit>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _commitRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SkipsExistingCommits()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetCommitsAsync("/tmp/test/repos/x", 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitCommitInfo { Sha = "existing", Message = "Old", CommittedAt = DateTime.UtcNow }]);
        _commitRepoMock.Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Commit, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _commitRepoMock.Verify(r => r.AddAsync(It.IsAny<Commit>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class ExtractSnippetsHandlerTests : IDisposable
{
    private readonly Andy.CodeIndex.Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<IChunkingService> _chunkingServiceMock = new();
    private readonly ExtractSnippetsHandler _handler;
    private readonly Repository _testRepo;

    public ExtractSnippetsHandlerTests()
    {
        _context = Helpers.TestDbContextFactory.Create();
        _handler = new ExtractSnippetsHandler(
            _context,
            _gitServiceMock.Object,
            _chunkingServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<ExtractSnippetsHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();

        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsExtractSnippets()
    {
        _handler.Operation.Should().Be(TaskOperation.ExtractSnippets);
    }

    [Fact]
    public async Task HandleAsync_FirstRun_AddsChunks()
    {
        SetupGitFiles([("Program.cs", "csharp", "public class Program { }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("public class Program { }", "Program.cs", null))
            .Returns([new CodeChunk { Content = "public class Program { }", StartLine = 1, EndLine = 1, FilePath = "Program.cs" }]);

        await RunHandler();

        _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ReRun_UnchangedFiles_DoesNotDuplicate()
    {
        SetupGitFiles([("A.cs", "csharp", "class A { }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);

        await RunHandler();
        var countAfterFirst = _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id);

        await RunHandler(); // Second run — same content
        var countAfterSecond = _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id);

        countAfterSecond.Should().Be(countAfterFirst, "re-indexing unchanged files must not create duplicates");
    }

    [Fact]
    public async Task HandleAsync_ModifiedFile_UpdatesContent_PreservesId()
    {
        SetupGitFiles([("A.cs", "csharp", "class A { int x; }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { int x; }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { int x; }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);

        await RunHandler();
        var originalId = _context.Enrichments.First(e => e.FilePath == "A.cs").Id;

        // Modify file content
        SetupGitFiles([("A.cs", "csharp", "class A { int x; int y; }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { int x; int y; }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { int x; int y; }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);

        await RunHandler();

        var updated = _context.Enrichments.First(e => e.FilePath == "A.cs");
        updated.Id.Should().Be(originalId, "modified chunk should preserve its ID (and attached embeddings)");
        updated.Content.Should().Contain("int y", "content should be updated");
    }

    [Fact]
    public async Task HandleAsync_DeletedFile_RemovesChunks()
    {
        SetupGitFiles([("A.cs", "csharp", "class A { }"), ("B.cs", "csharp", "class B { }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class B { }", "B.cs", null))
            .Returns([new CodeChunk { Content = "class B { }", StartLine = 1, EndLine = 1, FilePath = "B.cs" }]);

        await RunHandler();
        _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id).Should().Be(2);

        // Remove B.cs
        SetupGitFiles([("A.cs", "csharp", "class A { }")]);

        await RunHandler();

        _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id).Should().Be(1);
        _context.Enrichments.Should().NotContain(e => e.FilePath == "B.cs");
    }

    [Fact]
    public async Task HandleAsync_NewFile_AddsWithoutAffectingExisting()
    {
        SetupGitFiles([("A.cs", "csharp", "class A { }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class A { }", "A.cs", null))
            .Returns([new CodeChunk { Content = "class A { }", StartLine = 1, EndLine = 1, FilePath = "A.cs" }]);

        await RunHandler();
        var originalAId = _context.Enrichments.First(e => e.FilePath == "A.cs").Id;

        // Add B.cs
        SetupGitFiles([("A.cs", "csharp", "class A { }"), ("B.cs", "csharp", "class B { }")]);
        _chunkingServiceMock.Setup(c => c.ChunkText("class B { }", "B.cs", null))
            .Returns([new CodeChunk { Content = "class B { }", StartLine = 1, EndLine = 1, FilePath = "B.cs" }]);

        await RunHandler();

        _context.Enrichments.Count(e => e.RepositoryId == _testRepo.Id).Should().Be(2);
        _context.Enrichments.First(e => e.FilePath == "A.cs").Id.Should().Be(originalAId, "existing chunk should be preserved");
    }

    [Fact]
    public void ComputeHash_SameContent_SameHash()
    {
        ExtractSnippetsHandler.ComputeHash("hello").Should().Be(ExtractSnippetsHandler.ComputeHash("hello"));
    }

    [Fact]
    public void ComputeHash_DifferentContent_DifferentHash()
    {
        ExtractSnippetsHandler.ComputeHash("hello").Should().NotBe(ExtractSnippetsHandler.ComputeHash("world"));
    }

    private void SetupGitFiles(List<(string path, string language, string content)> files)
    {
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files.Select(f => new GitFileInfo { Path = f.path, Size = f.content.Length, Language = f.language }).ToList());
        foreach (var (path, _, content) in files)
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

public class CreateApiDocsHandlerTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IEnrichmentRepository> _enrichmentRepoMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<ICodeAnalysisService> _codeAnalysisMock = new();
    private readonly CreateApiDocsHandler _handler;
    private readonly Repository _testRepo;

    public CreateApiDocsHandlerTests()
    {
        _handler = new CreateApiDocsHandler(
            _repoRepoMock.Object,
            _enrichmentRepoMock.Object,
            _gitServiceMock.Object,
            _codeAnalysisMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<CreateApiDocsHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public void Operation_IsCreatePublicAPIDocs()
    {
        _handler.Operation.Should().Be(TaskOperation.CreatePublicAPIDocs);
    }

    [Fact]
    public async Task HandleAsync_GeneratesApiDocsForSupportedFiles()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitFileInfo { Path = "Service.cs", Size = 500, Language = "csharp" }]);
        _gitServiceMock.Setup(g => g.ReadFileAsync("/tmp/test/repos/x", "abc123", "Service.cs", It.IsAny<CancellationToken>()))
            .ReturnsAsync("public class MyService { }");
        _codeAnalysisMock.Setup(c => c.SupportsLanguage("csharp")).Returns(true);
        _codeAnalysisMock.Setup(c => c.Analyze("public class MyService { }", "Service.cs", "csharp"))
            .Returns(new CodeAnalysisResult
            {
                FilePath = "Service.cs", Language = "csharp",
                Classes = [new ApiClass { Name = "MyService" }]
            });
        _codeAnalysisMock.Setup(c => c.GenerateApiDocs(It.IsAny<CodeAnalysisResult>()))
            .Returns("# API: MyService");

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreatePublicAPIDocs, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _enrichmentRepoMock.Verify(r => r.AddAsync(
            It.Is<Enrichment>(e =>
                e.Type == EnrichmentType.Usage &&
                e.Subtype == EnrichmentSubtype.APIDocs &&
                e.Content == "# API: MyService"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SkipsUnsupportedLanguages()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitFileInfo { Path = "main.rs", Size = 500, Language = "rust" }]);
        _codeAnalysisMock.Setup(c => c.SupportsLanguage("rust")).Returns(false);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreatePublicAPIDocs, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _codeAnalysisMock.Verify(c => c.Analyze(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
