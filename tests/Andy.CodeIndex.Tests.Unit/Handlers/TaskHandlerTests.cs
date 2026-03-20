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

public class CloneRepositoryHandlerTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly CloneRepositoryHandler _handler;
    private readonly Repository _testRepo;

    public CloneRepositoryHandlerTests()
    {
        _handler = new CloneRepositoryHandler(
            _repoRepoMock.Object,
            _gitServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<CloneRepositoryHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public void Operation_IsCloneRepository()
    {
        _handler.Operation.Should().Be(TaskOperation.CloneRepository);
    }

    [Fact]
    public async Task HandleAsync_ClonesAndUpdatesBranches()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id))
            .Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.CloneAsync(_testRepo.Url, "/tmp/test/repos/x", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GetBranchesAsync("/tmp/test/repos/x", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitBranchInfo { Name = "main", HeadCommitSha = "abc123", IsDefault = true },
                new GitBranchInfo { Name = "develop", HeadCommitSha = "def456", IsDefault = false }
            ]);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CloneRepository, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _gitServiceMock.Verify(g => g.CloneAsync(_testRepo.Url, "/tmp/test/repos/x", null, It.IsAny<CancellationToken>()), Times.Once);
        _repoRepoMock.Verify(r => r.Update(It.IsAny<Repository>()), Times.Exactly(2)); // "cloning" then "cloned"
        _repoRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _testRepo.Status.Should().Be("cloned");
        _testRepo.Branches.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandleAsync_NonExistentRepo_Throws()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = Guid.NewGuid(),
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

public class ExtractSnippetsHandlerTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IEnrichmentRepository> _enrichmentRepoMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<IChunkingService> _chunkingServiceMock = new();
    private readonly ExtractSnippetsHandler _handler;
    private readonly Repository _testRepo;

    public ExtractSnippetsHandlerTests()
    {
        _handler = new ExtractSnippetsHandler(
            _repoRepoMock.Object,
            _enrichmentRepoMock.Object,
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
    }

    [Fact]
    public void Operation_IsExtractSnippets()
    {
        _handler.Operation.Should().Be(TaskOperation.ExtractSnippets);
    }

    [Fact]
    public async Task HandleAsync_ChunksFilesAndStoresEnrichments()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepo);
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.ListFilesAsync("/tmp/test/repos/x", "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitFileInfo { Path = "Program.cs", Size = 500, Language = "csharp" },
                new GitFileInfo { Path = "image.png", Size = 1000, Language = null } // No language = skipped
            ]);
        _gitServiceMock.Setup(g => g.ReadFileAsync("/tmp/test/repos/x", "abc123", "Program.cs", It.IsAny<CancellationToken>()))
            .ReturnsAsync("public class Program { }");
        _chunkingServiceMock.Setup(c => c.ChunkText("public class Program { }", "Program.cs", null))
            .Returns([
                new CodeChunk { Content = "public class Program { }", StartLine = 1, EndLine = 1, FilePath = "Program.cs" }
            ]);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractSnippets, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _enrichmentRepoMock.Verify(r => r.AddAsync(
            It.Is<Enrichment>(e =>
                e.Type == EnrichmentType.Development &&
                e.Subtype == EnrichmentSubtype.Chunk &&
                e.Language == "csharp"),
            It.IsAny<CancellationToken>()), Times.Once);
        _enrichmentRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
