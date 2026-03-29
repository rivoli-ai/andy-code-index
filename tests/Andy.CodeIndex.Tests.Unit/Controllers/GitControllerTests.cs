using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Controllers;

public class GitControllerTests : IDisposable
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<ICommitRepository> _commitRepoMock = new();
    private readonly CodeIndexDbContext _dbContext;
    private readonly GitController _controller;
    private readonly Guid _repoId = Guid.NewGuid();
    private readonly string _cloneDir;

    public GitControllerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        var options = Options.Create(new IndexingOptions { DataDir = "/tmp/test-data" });
        _cloneDir = $"/tmp/test-data/repos/{_repoId}";

        _controller = new GitController(
            _repoRepoMock.Object,
            _gitServiceMock.Object,
            _commitRepoMock.Object,
            _dbContext,
            options);

        _repoRepoMock.Setup(r => r.GetByIdAsync(_repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Repository
            {
                Id = _repoId, Name = "test-repo", Url = "https://github.com/test/repo",
                Provider = GitProvider.GitHub, DefaultBranch = "main",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test-data", _repoId))
            .Returns(_cloneDir);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // --- GitLog Tests ---

    [Fact]
    public async Task GitLog_RepoNotFound_Returns404()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var result = await _controller.GitLog(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GitLog_NegativeLimit_Returns400()
    {
        var result = await _controller.GitLog(_repoId, limit: -1);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GitLog_LimitOver500_Returns400()
    {
        var result = await _controller.GitLog(_repoId, limit: 501);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GitLog_RepoNotCloned_Returns409()
    {
        // GetCloneDir returns a path that doesn't exist, simulating not cloned
        _gitServiceMock.Setup(g => g.GetCloneDir(It.IsAny<string>(), _repoId))
            .Returns("/nonexistent/path");

        var result = await _controller.GitLog(_repoId);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GitLog_InvalidRef_Returns404()
    {
        var dir = SetupCloneDirExists();
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _controller.GitLog(_repoId, gitRef: "main");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GitLog_ValidRef_ReturnsPaginatedCommits()
    {
        var dir = SetupCloneDirExists();
        var sha = "abc1234567890abcdef1234567890abcdef123456";
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sha);
        _gitServiceMock.Setup(g => g.GetCommitsAsync(dir, "main", 51, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitCommitInfo
                {
                    Sha = sha,
                    Message = "Initial commit",
                    AuthorName = "Test",
                    AuthorEmail = "test@test.com",
                    CommittedAt = DateTime.UtcNow,
                    ParentShas = []
                }
            ]);

        var result = await _controller.GitLog(_repoId);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitLogResponseDto>().Subject;
        response.Commits.Should().HaveCount(1);
        response.HasMore.Should().BeFalse();
        response.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GitLog_CursorPagination_ReturnsNextPage()
    {
        var dir = SetupCloneDirExists();
        var sha1 = "abc1234567890abcdef1234567890abcdef123456";
        var sha2 = "def1234567890abcdef1234567890abcdef123456";
        var cursorSha = "111111111111111111111111111111111111111111";

        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sha1);
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, cursorSha, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorSha);
        _gitServiceMock.Setup(g => g.GetCommitsAsync(dir, "main", 2, cursorSha, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitCommitInfo { Sha = sha1, Message = "Commit 1", CommittedAt = DateTime.UtcNow },
                new GitCommitInfo { Sha = sha2, Message = "Commit 2", CommittedAt = DateTime.UtcNow }
            ]);

        var result = await _controller.GitLog(_repoId, limit: 1, before: cursorSha);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitLogResponseDto>().Subject;
        response.Commits.Should().HaveCount(1);
        response.HasMore.Should().BeTrue();
        response.NextCursor.Should().Be(sha1);
    }

    [Fact]
    public async Task GitLog_IncludesEnrichmentCounts()
    {
        var dir = SetupCloneDirExists();
        var sha = "abc1234567890abcdef1234567890abcdef123456";
        var commitId = Guid.NewGuid();

        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sha);
        _gitServiceMock.Setup(g => g.GetCommitsAsync(dir, "main", 51, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitCommitInfo { Sha = sha, Message = "test", CommittedAt = DateTime.UtcNow }
            ]);

        // Seed DB with commit and enrichments
        var repo = new Repository { Id = _repoId, Name = "test", Url = "https://a.com", Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _dbContext.Repositories.Add(repo);

        var commit = new Commit { Id = commitId, RepositoryId = _repoId, Sha = sha, Message = "test", CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, IsIndexed = true };
        _dbContext.Commits.Add(commit);

        _dbContext.Enrichments.Add(new Enrichment { Id = Guid.NewGuid(), RepositoryId = _repoId, CommitId = commitId, Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk, Content = "test" });
        _dbContext.Enrichments.Add(new Enrichment { Id = Guid.NewGuid(), RepositoryId = _repoId, CommitId = commitId, Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Snippet, Content = "test" });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GitLog(_repoId);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitLogResponseDto>().Subject;
        response.Commits[0].IsIndexed.Should().BeTrue();
        response.Commits[0].EnrichmentCount.Should().Be(2);
    }

    // --- GitRefs Tests ---

    [Fact]
    public async Task GitRefs_RepoNotFound_Returns404()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var result = await _controller.GitRefs(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GitRefs_RepoNotCloned_Returns409()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir(It.IsAny<string>(), _repoId))
            .Returns("/nonexistent/path");

        var result = await _controller.GitRefs(_repoId);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GitRefs_ReturnsBranchesAndTags()
    {
        var dir = SetupCloneDirExists();
        _gitServiceMock.Setup(g => g.GetBranchesAsync(dir, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitBranchInfo { Name = "main", HeadCommitSha = "abc123", IsDefault = true },
                new GitBranchInfo { Name = "develop", HeadCommitSha = "def456", IsDefault = false }
            ]);
        _gitServiceMock.Setup(g => g.GetTagsAsync(dir, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitTagInfo { Name = "v1.0.0", CommitSha = "abc123" }
            ]);
        _gitServiceMock.Setup(g => g.GetHeadRefAsync(dir, It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123");

        var result = await _controller.GitRefs(_repoId);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitRefsResponseDto>().Subject;
        response.Branches.Should().HaveCount(2);
        response.Tags.Should().HaveCount(1);
        response.Head.Should().Be("abc123");
        response.Branches[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task GitRefs_IdentifiesHead()
    {
        var dir = SetupCloneDirExists();
        _gitServiceMock.Setup(g => g.GetBranchesAsync(dir, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _gitServiceMock.Setup(g => g.GetTagsAsync(dir, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _gitServiceMock.Setup(g => g.GetHeadRefAsync(dir, It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123def456");

        var result = await _controller.GitRefs(_repoId);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitRefsResponseDto>().Subject;
        response.Head.Should().Be("abc123def456");
    }

    // --- GitTree Tests ---

    [Fact]
    public async Task GitTree_RepoNotFound_Returns404()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var result = await _controller.GitTree(Guid.NewGuid(), "main");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GitTree_InvalidRef_Returns404()
    {
        var dir = SetupCloneDirExists();
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _controller.GitTree(_repoId, "nonexistent");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GitTree_ValidRef_ReturnsFiles()
    {
        var dir = SetupCloneDirExists();
        var sha = "abc1234567890abcdef1234567890abcdef123456";
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sha);
        _gitServiceMock.Setup(g => g.ListTreeAsync(dir, "main", null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitTreeEntry { Path = "src", Name = "src", Type = "tree", Hash = "hash1" },
                new GitTreeEntry { Path = "README.md", Name = "README.md", Type = "blob", Hash = "hash2", Size = 100, Language = "markdown" }
            ]);
        _commitRepoMock.Setup(c => c.GetByShaAsync(_repoId, sha, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commit?)null);

        var result = await _controller.GitTree(_repoId, "main");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitTreeResponseDto>().Subject;
        response.Entries.Should().HaveCount(2);
        response.Ref.Should().Be("main");
        response.Recursive.Should().BeFalse();
    }

    [Fact]
    public async Task GitTree_NonRecursive_ReturnsDirectories()
    {
        var dir = SetupCloneDirExists();
        var sha = "abc1234567890abcdef1234567890abcdef123456";
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sha);
        _gitServiceMock.Setup(g => g.ListTreeAsync(dir, "main", null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitTreeEntry { Path = "src", Name = "src", Type = "tree", Hash = "hash1" },
                new GitTreeEntry { Path = "tests", Name = "tests", Type = "tree", Hash = "hash2" },
                new GitTreeEntry { Path = "README.md", Name = "README.md", Type = "blob", Hash = "hash3", Size = 50 }
            ]);
        _commitRepoMock.Setup(c => c.GetByShaAsync(_repoId, sha, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commit?)null);

        var result = await _controller.GitTree(_repoId, "main");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitTreeResponseDto>().Subject;
        response.Entries.Count(e => e.Type == "tree").Should().Be(2);
        response.Entries.Count(e => e.Type == "blob").Should().Be(1);
    }

    [Fact]
    public async Task GitTree_WithPath_FiltersToSubdirectory()
    {
        var dir = SetupCloneDirExists();
        var sha = "abc1234567890abcdef1234567890abcdef123456";
        _gitServiceMock.Setup(g => g.ResolveRefAsync(dir, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sha);
        _gitServiceMock.Setup(g => g.ListTreeAsync(dir, "main", "src", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitTreeEntry { Path = "src/Program.cs", Name = "Program.cs", Type = "blob", Hash = "hash1", Size = 200, Language = "csharp" }
            ]);
        _commitRepoMock.Setup(c => c.GetByShaAsync(_repoId, sha, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commit?)null);

        var result = await _controller.GitTree(_repoId, "main", path: "src");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GitTreeResponseDto>().Subject;
        response.Path.Should().Be("src");
        response.Entries.Should().HaveCount(1);
        response.Entries[0].Name.Should().Be("Program.cs");
    }

    // --- CommitSummary Tests ---

    [Fact]
    public async Task CommitSummary_NonExistentRepo_Returns404()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        // Use the CommitsController for summary
        var commitsController = new CommitsController(
            _commitRepoMock.Object, _repoRepoMock.Object,
            Mock.Of<ICommitComparisonService>(), _dbContext);

        var result = await commitsController.GetCommitSummary(Guid.NewGuid(), "abc123");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CommitSummary_InvalidSha_Returns404()
    {
        _commitRepoMock.Setup(c => c.GetByShaAsync(_repoId, "notfound", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commit?)null);

        var commitsController = new CommitsController(
            _commitRepoMock.Object, _repoRepoMock.Object,
            Mock.Of<ICommitComparisonService>(), _dbContext);

        var result = await commitsController.GetCommitSummary(_repoId, "notfound");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CommitSummary_IndexedCommit_ReturnsBreakdown()
    {
        var commitId = Guid.NewGuid();
        var commit = new Commit
        {
            Id = commitId, RepositoryId = _repoId, Sha = "abc123",
            Message = "test", CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, IsIndexed = true
        };

        _commitRepoMock.Setup(c => c.GetByShaAsync(_repoId, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(commit);

        // Seed DB with enrichments and files
        var repo = new Repository { Id = _repoId, Name = "test", Url = "https://a.com", Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _dbContext.Repositories.Add(repo);
        _dbContext.Commits.Add(commit);
        _dbContext.Enrichments.Add(new Enrichment { Id = Guid.NewGuid(), RepositoryId = _repoId, CommitId = commitId, Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk, Content = "chunk1" });
        _dbContext.Enrichments.Add(new Enrichment { Id = Guid.NewGuid(), RepositoryId = _repoId, CommitId = commitId, Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk, Content = "chunk2" });
        _dbContext.Enrichments.Add(new Enrichment { Id = Guid.NewGuid(), RepositoryId = _repoId, CommitId = commitId, Type = EnrichmentType.Architecture, Subtype = EnrichmentSubtype.Physical, Content = "arch" });
        _dbContext.RepositoryFiles.Add(new RepositoryFile { Id = Guid.NewGuid(), CommitId = commitId, Path = "src/file.cs", CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        var commitsController = new CommitsController(
            _commitRepoMock.Object, _repoRepoMock.Object,
            Mock.Of<ICommitComparisonService>(), _dbContext);

        var result = await commitsController.GetCommitSummary(_repoId, "abc123");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CommitSummaryResponseDto>().Subject;
        response.Sha.Should().Be("abc123");
        response.IsIndexed.Should().BeTrue();
        response.TotalEnrichments.Should().Be(3);
        response.FilesIndexed.Should().Be(1);
        response.CountsBySubtype.Should().ContainKey("Chunk").WhoseValue.Should().Be(2);
        response.CountsBySubtype.Should().ContainKey("Physical").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task CommitSummary_NonIndexedCommit_ReturnsZeroes()
    {
        var commitId = Guid.NewGuid();
        var commit = new Commit
        {
            Id = commitId, RepositoryId = _repoId, Sha = "def456",
            Message = "empty commit", CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, IsIndexed = false
        };

        _commitRepoMock.Setup(c => c.GetByShaAsync(_repoId, "def456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(commit);

        var repo = new Repository { Id = _repoId, Name = "test", Url = "https://b.com", Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _dbContext.Repositories.Add(repo);
        _dbContext.Commits.Add(commit);
        await _dbContext.SaveChangesAsync();

        var commitsController = new CommitsController(
            _commitRepoMock.Object, _repoRepoMock.Object,
            Mock.Of<ICommitComparisonService>(), _dbContext);

        var result = await commitsController.GetCommitSummary(_repoId, "def456");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CommitSummaryResponseDto>().Subject;
        response.IsIndexed.Should().BeFalse();
        response.TotalEnrichments.Should().Be(0);
        response.FilesIndexed.Should().Be(0);
        response.EmbeddingsCount.Should().Be(0);
        response.CountsBySubtype.Should().BeEmpty();
    }

    // --- Helpers ---

    private string SetupCloneDirExists()
    {
        // The controller checks Directory.Exists(cloneDir).
        // We mock GetCloneDir to return a path that exists.
        var tempDir = Path.Combine(Path.GetTempPath(), $"git-test-{_repoId}");
        Directory.CreateDirectory(tempDir);
        _gitServiceMock.Setup(g => g.GetCloneDir(It.IsAny<string>(), _repoId))
            .Returns(tempDir);
        return tempDir;
    }
}
