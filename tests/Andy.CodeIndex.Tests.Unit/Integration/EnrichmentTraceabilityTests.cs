using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Integration;

/// <summary>
/// Integration tests verifying enrichment-commit traceability across the full pipeline.
/// Uses FakeGitService with a deterministic 5-commit history, real handlers, and an in-memory DB.
/// </summary>
public class EnrichmentTraceabilityTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly FakeGitService _gitService;
    private readonly ScanCommitHandler _scanHandler;
    private readonly ExtractSnippetsHandler _extractHandler;
    private readonly Repository _testRepo;
    private readonly string _dataDir = "/tmp/test-traceability";

    // Track created commit entities for assertions
    private readonly Dictionary<string, Commit> _commitEntities = new();

    public EnrichmentTraceabilityTests()
    {
        _context = TestDbContextFactory.Create();
        _gitService = new FakeGitService();

        // Use real chunking service
        var chunkingService = new ChunkingService();

        // Use permissive file filter (skip nothing)
        var fileFilterService = new FileFilterService(
            Options.Create(new FileFilterOptions
            {
                SkipExtensions = [],
                SkipPatterns = [],
                MaxFileSizeBytes = 10_000_000
            }));

        var indexingOptions = Options.Create(new IndexingOptions { DataDir = _dataDir });

        // ScanCommitHandler needs ICodeRepositoryRepository and ICommitRepository mocks
        var repoRepoMock = new Mock<ICodeRepositoryRepository>();
        var commitRepoMock = new Mock<ICommitRepository>();

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "fake-test-repo",
            Url = "https://github.com/test/fake-repo",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();

        repoRepoMock.Setup(r => r.GetByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _context.Repositories.Find(_testRepo.Id)!);

        commitRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Commit, bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((System.Linq.Expressions.Expression<Func<Commit, bool>> predicate, CancellationToken _) =>
                Task.FromResult(_context.Commits.Any(predicate)));

        commitRepoMock.Setup(r => r.AddAsync(It.IsAny<Commit>(), It.IsAny<CancellationToken>()))
            .Returns((Commit c, CancellationToken _) =>
            {
                _context.Commits.Add(c);
                return Task.FromResult(c);
            });

        commitRepoMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => _context.SaveChangesAsync());

        _scanHandler = new ScanCommitHandler(
            repoRepoMock.Object,
            commitRepoMock.Object,
            _gitService,
            _context,
            indexingOptions,
            NullLogger<ScanCommitHandler>.Instance);

        _extractHandler = new ExtractSnippetsHandler(
            _context,
            _gitService,
            chunkingService,
            fileFilterService,
            indexingOptions,
            NullLogger<ExtractSnippetsHandler>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ---------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a Commit entity and its RepositoryFile records for a given FakeGitService commit SHA.
    /// Also sets the commit as the "previous" for skip-if-unchanged logic.
    /// </summary>
    private async Task<Commit> SeedCommitWithFiles(string commitSha, DateTime? committedAt = null)
    {
        // Check if commit already exists
        var existing = await _context.Commits.FirstOrDefaultAsync(c =>
            c.RepositoryId == _testRepo.Id && c.Sha == commitSha);
        if (existing != null)
            return existing;

        var commitInfos = FakeGitService.GetAllCommits();
        var info = commitInfos.First(c => c.Sha == commitSha);

        var commit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = commitSha,
            Message = info.Message,
            AuthorName = info.AuthorName,
            AuthorEmail = info.AuthorEmail,
            CommittedAt = committedAt ?? info.CommittedAt,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(commit);

        // Create RepositoryFile records from FakeGitService
        var files = await _gitService.ListFilesAsync(_dataDir, commitSha);
        foreach (var file in files)
        {
            _context.RepositoryFiles.Add(new RepositoryFile
            {
                Id = Guid.NewGuid(),
                CommitId = commit.Id,
                Path = file.Path,
                Language = file.Language,
                Size = file.Size,
                Hash = file.Hash,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        _commitEntities[commitSha] = commit;
        return commit;
    }

    /// <summary>
    /// Seeds the commit and its predecessor (for skip-if-unchanged logic),
    /// then runs ExtractSnippetsHandler at the given commit SHA.
    /// </summary>
    private async Task RunExtractAtCommit(string commitSha, string? previousCommitSha = null)
    {
        // Seed predecessor if specified (needed for skip-if-unchanged comparison)
        if (previousCommitSha != null)
            await SeedCommitWithFiles(previousCommitSha);

        // Seed target commit
        await SeedCommitWithFiles(commitSha);

        // Set repo to extract at this commit
        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastIndexedCommitSha = commitSha;
        await _context.SaveChangesAsync();

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractSnippets,
            CreatedAt = DateTime.UtcNow
        };
        await _extractHandler.HandleAsync(task);
    }

    private List<Enrichment> GetChunkEnrichments()
        => _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id
                        && e.Type == EnrichmentType.Development
                        && e.Subtype == EnrichmentSubtype.Chunk)
            .ToList();

    private List<RepositoryFile> GetRepositoryFiles()
        => _context.RepositoryFiles.ToList();

    // ---------------------------------------------------------------
    // Test 1: Commit 1 -- all files get chunks with CommitId
    // ---------------------------------------------------------------

    [Fact]
    public async Task Commit1_AllFilesGetChunks_WithCommitId()
    {
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var enrichments = GetChunkEnrichments();

        // Commit 1 has README.md (markdown) and src/main.py (python)
        enrichments.Should().NotBeEmpty("commit 1 adds two files that should produce chunks");

        var readmeChunks = enrichments.Where(e => e.FilePath == "README.md").ToList();
        var mainPyChunks = enrichments.Where(e => e.FilePath == "src/main.py").ToList();

        readmeChunks.Should().NotBeEmpty("README.md should produce at least one chunk");
        mainPyChunks.Should().NotBeEmpty("src/main.py should produce at least one chunk");

        // All enrichments must have a CommitId pointing to commit 1
        var commitRecord = _commitEntities[FakeGitService.Commit1Sha];
        enrichments.Should().AllSatisfy(e =>
            e.CommitId.Should().Be(commitRecord.Id, "every enrichment should reference the commit"));
    }

    // ---------------------------------------------------------------
    // Test 2: Commit 2 -- only modified file is re-chunked
    // ---------------------------------------------------------------

    [Fact]
    public async Task Commit2_OnlyModifiedFileReChunked()
    {
        // Process commit 1 first (no predecessor)
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var enrichmentsAfterC1 = GetChunkEnrichments();
        var readmeChunkIdsC1 = enrichmentsAfterC1
            .Where(e => e.FilePath == "README.md")
            .Select(e => e.Id)
            .ToHashSet();
        var readmeChunkCountC1 = readmeChunkIdsC1.Count;

        // Process commit 2 (main.py modified, README unchanged blob SHA)
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);

        var enrichmentsAfterC2 = GetChunkEnrichments();

        // README chunks should still exist (skip-if-unchanged preserves them)
        var readmeChunksC2 = enrichmentsAfterC2.Where(e => e.FilePath == "README.md").ToList();
        readmeChunksC2.Should().HaveCount(readmeChunkCountC1,
            "README.md is unchanged between commit 1 and 2, chunks should be preserved");

        // README chunk IDs should be the same (not recreated)
        readmeChunksC2.Select(e => e.Id).Should().BeSubsetOf(readmeChunkIdsC1,
            "README.md chunks should keep their original IDs since the file is unchanged");

        // main.py chunks should have new CommitId for commit 2
        var commit2Record = _commitEntities[FakeGitService.Commit2Sha];
        var mainPyChunksC2 = enrichmentsAfterC2.Where(e => e.FilePath == "src/main.py").ToList();
        mainPyChunksC2.Should().NotBeEmpty("main.py should have chunks after commit 2");
        mainPyChunksC2.Should().AllSatisfy(e =>
            e.CommitId.Should().Be(commit2Record.Id, "main.py was modified, CommitId should reference commit 2"));
    }

    // ---------------------------------------------------------------
    // Test 3: Commit 3 -- new file chunked, existing untouched
    // ---------------------------------------------------------------

    [Fact]
    public async Task Commit3_NewFileChunked_ExistingUntouched()
    {
        // Process commits 1 and 2
        await RunExtractAtCommit(FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);

        var enrichmentsBeforeC3 = GetChunkEnrichments();
        var mainPyIdsBeforeC3 = enrichmentsBeforeC3
            .Where(e => e.FilePath == "src/main.py")
            .Select(e => e.Id)
            .ToHashSet();

        // Process commit 3 (adds src/utils.py, others unchanged)
        await RunExtractAtCommit(FakeGitService.Commit3Sha, previousCommitSha: FakeGitService.Commit2Sha);

        var enrichmentsAfterC3 = GetChunkEnrichments();

        // utils.py should have new chunks
        var utilsChunks = enrichmentsAfterC3.Where(e => e.FilePath == "src/utils.py").ToList();
        utilsChunks.Should().NotBeEmpty("utils.py is a new file in commit 3 and should produce chunks");

        // main.py chunk IDs should be preserved (same blob SHA between commit 2 and 3)
        var mainPyChunksC3 = enrichmentsAfterC3.Where(e => e.FilePath == "src/main.py").ToList();
        mainPyChunksC3.Select(e => e.Id).Should().BeSubsetOf(mainPyIdsBeforeC3,
            "main.py is unchanged between commit 2 and 3, chunk IDs should be preserved");
    }

    // ---------------------------------------------------------------
    // Test 4: Commit 4 -- deleted file handled, modified file re-chunked
    // ---------------------------------------------------------------

    [Fact]
    public async Task Commit4_DeletedFileEnrichmentsRemoved()
    {
        // Process commits 1 through 3
        await RunExtractAtCommit(FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit3Sha, previousCommitSha: FakeGitService.Commit2Sha);

        // Verify README chunks exist before commit 4
        var readmeChunksBefore = GetChunkEnrichments().Where(e => e.FilePath == "README.md").ToList();
        readmeChunksBefore.Should().NotBeEmpty("README.md should have chunks from earlier commits");

        // Process commit 4 (deletes README.md, modifies main.py)
        await RunExtractAtCommit(FakeGitService.Commit4Sha, previousCommitSha: FakeGitService.Commit3Sha);

        var enrichmentsAfterC4 = GetChunkEnrichments();

        // README.md chunks should be removed (file no longer in tree)
        var readmeChunksAfter = enrichmentsAfterC4.Where(e => e.FilePath == "README.md").ToList();
        readmeChunksAfter.Should().BeEmpty("README.md was deleted in commit 4, its chunks should be removed");

        // main.py should be re-chunked with commit 4's CommitId
        var commit4Record = _commitEntities[FakeGitService.Commit4Sha];
        var mainPyChunks = enrichmentsAfterC4.Where(e => e.FilePath == "src/main.py").ToList();
        mainPyChunks.Should().NotBeEmpty("main.py was modified and should have chunks");
        mainPyChunks.Should().AllSatisfy(e =>
            e.CommitId.Should().Be(commit4Record.Id));

        // utils.py should be unchanged
        var utilsChunks = enrichmentsAfterC4.Where(e => e.FilePath == "src/utils.py").ToList();
        utilsChunks.Should().NotBeEmpty("utils.py is unchanged and should still have chunks");
    }

    // ---------------------------------------------------------------
    // Test 5: Commit 5 -- empty commit, no new enrichments
    // ---------------------------------------------------------------

    [Fact]
    public async Task Commit5_EmptyCommit_NoNewEnrichments()
    {
        // Process commits 1 through 4
        await RunExtractAtCommit(FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit3Sha, previousCommitSha: FakeGitService.Commit2Sha);
        await RunExtractAtCommit(FakeGitService.Commit4Sha, previousCommitSha: FakeGitService.Commit3Sha);

        var enrichmentsBefore = GetChunkEnrichments();
        var enrichmentIdsBefore = enrichmentsBefore.Select(e => e.Id).ToHashSet();
        var countBefore = enrichmentsBefore.Count;

        // Process commit 5 (empty commit -- same files as commit 4)
        await RunExtractAtCommit(FakeGitService.Commit5Sha, previousCommitSha: FakeGitService.Commit4Sha);

        var enrichmentsAfter = GetChunkEnrichments();

        // Same number of enrichments (nothing added or removed)
        enrichmentsAfter.Should().HaveCount(countBefore,
            "commit 5 is empty; no enrichments should be added or removed");

        // Same IDs preserved
        enrichmentsAfter.Select(e => e.Id).Should().BeEquivalentTo(enrichmentIdsBefore,
            "commit 5 changes nothing; enrichment IDs should be preserved");
    }

    // ---------------------------------------------------------------
    // Test 6: All enrichments have CommitId after full pipeline
    // ---------------------------------------------------------------

    [Fact]
    public async Task AllEnrichments_HaveCommitId_AfterFullPipeline()
    {
        await RunExtractAtCommit(FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit3Sha, previousCommitSha: FakeGitService.Commit2Sha);
        await RunExtractAtCommit(FakeGitService.Commit4Sha, previousCommitSha: FakeGitService.Commit3Sha);
        await RunExtractAtCommit(FakeGitService.Commit5Sha, previousCommitSha: FakeGitService.Commit4Sha);

        var enrichments = GetChunkEnrichments();
        enrichments.Should().NotBeEmpty("pipeline should have created enrichments");

        enrichments.Should().AllSatisfy(e =>
            e.CommitId.Should().NotBeNull("every enrichment must be traceable to a commit"));
    }

    // ---------------------------------------------------------------
    // Test 7: RepositoryFiles have blob SHAs
    // ---------------------------------------------------------------

    [Fact]
    public async Task RepositoryFiles_HaveBlobSha()
    {
        await SeedCommitWithFiles(FakeGitService.Commit1Sha);

        var repoFiles = GetRepositoryFiles();
        repoFiles.Should().NotBeEmpty("SeedCommitWithFiles should create RepositoryFile records");

        repoFiles.Should().AllSatisfy(f =>
        {
            f.Hash.Should().NotBeNullOrEmpty("every RepositoryFile must have a blob SHA");
            f.Path.Should().NotBeNullOrEmpty("every RepositoryFile must have a path");
        });

        // Verify specific files for commit 1
        repoFiles.Should().Contain(f => f.Path == "README.md" && f.Hash == "blob-readme-v1");
        repoFiles.Should().Contain(f => f.Path == "src/main.py" && f.Hash == "blob-main-v1");
    }

    // ---------------------------------------------------------------
    // Test 8: SkipUnchanged preserves existing enrichment
    // ---------------------------------------------------------------

    [Fact]
    public async Task SkipUnchanged_PreservesExistingEnrichment()
    {
        // Process commit 1
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var commit1Record = _commitEntities[FakeGitService.Commit1Sha];
        var readmeChunksC1 = GetChunkEnrichments().Where(e => e.FilePath == "README.md").ToList();
        readmeChunksC1.Should().NotBeEmpty();

        var originalIds = readmeChunksC1.Select(e => e.Id).ToList();
        var originalCommitId = readmeChunksC1.First().CommitId;
        originalCommitId.Should().Be(commit1Record.Id);

        // Process commit 2 (README unchanged, main.py modified)
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);

        // README enrichments should still be there with same IDs
        var readmeChunksC2 = GetChunkEnrichments().Where(e => e.FilePath == "README.md").ToList();
        readmeChunksC2.Select(e => e.Id).Should().BeEquivalentTo(originalIds,
            "unchanged file should preserve enrichment IDs across commits");
    }

    // ---------------------------------------------------------------
    // Test 9: Empty repository produces no errors
    // ---------------------------------------------------------------

    [Fact]
    public async Task EmptyRepository_NoErrors()
    {
        // Create a separate repo entity that will have no files
        var emptyRepo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "empty-repo",
            Url = "https://github.com/test/empty-repo",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(emptyRepo);
        await _context.SaveChangesAsync();

        // Create a commit with no files (SHA not in FakeGitService's file map)
        var emptyCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = emptyRepo.Id,
            Sha = "ffff000000ffff000000ffff000000ffff000000",
            Message = "Empty repo commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(emptyCommit);
        emptyRepo.LastIndexedCommitSha = emptyCommit.Sha;
        await _context.SaveChangesAsync();

        // Run extract snippets -- should complete without errors
        var extractTask = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = emptyRepo.Id,
            Operation = TaskOperation.ExtractSnippets,
            CreatedAt = DateTime.UtcNow
        };

        var act = () => _extractHandler.HandleAsync(extractTask);
        await act.Should().NotThrowAsync("empty repository should not cause errors");

        // No enrichments created
        var enrichments = _context.Enrichments
            .Where(e => e.RepositoryId == emptyRepo.Id)
            .ToList();
        enrichments.Should().BeEmpty("empty repository should produce zero enrichments");
    }

    // ---------------------------------------------------------------
    // Test 10: After wiping enrichments, pipeline regenerates all
    // ---------------------------------------------------------------

    [Fact]
    public async Task AfterWipeEnrichments_RegeneratesAll()
    {
        // Process commit 1
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var enrichmentsBefore = GetChunkEnrichments();
        var countBefore = enrichmentsBefore.Count;
        countBefore.Should().BeGreaterThan(0, "commit 1 should create enrichments");

        // Wipe all enrichments
        _context.Enrichments.RemoveRange(_context.Enrichments.Where(e => e.RepositoryId == _testRepo.Id));
        await _context.SaveChangesAsync();
        GetChunkEnrichments().Should().BeEmpty("all enrichments should be wiped");

        // Re-run extract at commit 1 (commit entity still exists)
        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastIndexedCommitSha = FakeGitService.Commit1Sha;
        await _context.SaveChangesAsync();

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractSnippets,
            CreatedAt = DateTime.UtcNow
        };
        await _extractHandler.HandleAsync(task);

        var enrichmentsAfter = GetChunkEnrichments();
        enrichmentsAfter.Should().HaveCount(countBefore,
            "after wipe, re-processing should regenerate the same number of enrichments");
    }

    // ---------------------------------------------------------------
    // Test 11: Processing same commit twice creates zero new enrichments
    // ---------------------------------------------------------------

    [Fact]
    public async Task NothingToDo_SameCommitTwice_NoNewEnrichments()
    {
        // Process commit 1
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var enrichmentsFirst = GetChunkEnrichments().ToList();
        var idsFirst = enrichmentsFirst.Select(e => e.Id).ToHashSet();
        var countFirst = enrichmentsFirst.Count;
        countFirst.Should().BeGreaterThan(0);

        // Process commit 1 again (same SHA, same files)
        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastIndexedCommitSha = FakeGitService.Commit1Sha;
        await _context.SaveChangesAsync();

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractSnippets,
            CreatedAt = DateTime.UtcNow
        };
        await _extractHandler.HandleAsync(task);

        var enrichmentsSecond = GetChunkEnrichments().ToList();
        enrichmentsSecond.Should().HaveCount(countFirst,
            "processing the same commit again should not create new enrichments");

        enrichmentsSecond.Select(e => e.Id).Should().BeEquivalentTo(idsFirst,
            "enrichment IDs should remain the same on repeated processing");

        // Check indexing run stats
        var runs = _context.IndexingRuns
            .Where(r => r.RepositoryId == _testRepo.Id)
            .OrderBy(r => r.CompletedAt)
            .ToList();
        runs.Should().HaveCount(2, "two extraction runs should be recorded");

        var secondRun = runs.Last();
        secondRun.SnippetsAdded.Should().Be(0, "second run should add zero snippets");
    }

    // ---------------------------------------------------------------
    // Test 12: ScanCommitHandler creates correct commit records
    // ---------------------------------------------------------------

    [Fact]
    public async Task ScanCommit_CreatesAllCommitRecords()
    {
        // Set no prior commits
        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastIndexedCommitSha = null;
        await _context.SaveChangesAsync();

        var scanTask = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit,
            CreatedAt = DateTime.UtcNow
        };
        await _scanHandler.HandleAsync(scanTask);

        var commits = _context.Commits
            .Where(c => c.RepositoryId == _testRepo.Id)
            .OrderByDescending(c => c.CommittedAt)
            .ToList();

        // Should have all 5 commits
        commits.Should().HaveCount(5, "all 5 commits from FakeGitService should be recorded");

        commits.Should().Contain(c => c.Sha == FakeGitService.Commit1Sha);
        commits.Should().Contain(c => c.Sha == FakeGitService.Commit2Sha);
        commits.Should().Contain(c => c.Sha == FakeGitService.Commit3Sha);
        commits.Should().Contain(c => c.Sha == FakeGitService.Commit4Sha);
        commits.Should().Contain(c => c.Sha == FakeGitService.Commit5Sha);
    }

    // ---------------------------------------------------------------
    // Test 13: ScanCommitHandler creates RepositoryFile records for latest commit
    // ---------------------------------------------------------------

    [Fact]
    public async Task ScanCommit_CreatesRepositoryFiles_ForLatestCommit()
    {
        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastIndexedCommitSha = null;
        await _context.SaveChangesAsync();

        var scanTask = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit,
            CreatedAt = DateTime.UtcNow
        };
        await _scanHandler.HandleAsync(scanTask);

        // ScanCommitHandler creates files for the latest NEW commit (first in git log = commit 5)
        var repoFiles = GetRepositoryFiles();
        repoFiles.Should().NotBeEmpty("ScanCommitHandler should create file records");

        // Commit 5 has: src/main.py and src/utils.py (same as commit 4)
        repoFiles.Should().Contain(f => f.Path == "src/main.py");
        repoFiles.Should().Contain(f => f.Path == "src/utils.py");
    }

    // ---------------------------------------------------------------
    // Test 14: Incremental scan does not duplicate commits
    // ---------------------------------------------------------------

    [Fact]
    public async Task ScanCommit_Incremental_DoesNotDuplicateCommits()
    {
        // First scan: all commits
        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastIndexedCommitSha = null;
        await _context.SaveChangesAsync();

        var scanTask1 = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit,
            CreatedAt = DateTime.UtcNow
        };
        await _scanHandler.HandleAsync(scanTask1);
        var countAfterFirst = _context.Commits.Count(c => c.RepositoryId == _testRepo.Id);
        countAfterFirst.Should().Be(5);

        // Second scan with sinceSha = commit 3 => returns commits 4 and 5
        // But they already exist, so no new commits should be added
        repo.LastIndexedCommitSha = FakeGitService.Commit3Sha;
        await _context.SaveChangesAsync();

        var scanTask2 = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ScanCommit,
            CreatedAt = DateTime.UtcNow
        };
        await _scanHandler.HandleAsync(scanTask2);
        var countAfterSecond = _context.Commits.Count(c => c.RepositoryId == _testRepo.Id);
        countAfterSecond.Should().Be(5, "re-scanning should not duplicate commits");
    }

    // ---------------------------------------------------------------
    // Test 15: IndexingRun records statistics correctly
    // ---------------------------------------------------------------

    [Fact]
    public async Task IndexingRun_RecordsCorrectStatistics()
    {
        // Process commit 1 (initial)
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var run = _context.IndexingRuns
            .Where(r => r.RepositoryId == _testRepo.Id)
            .OrderByDescending(r => r.CompletedAt)
            .First();

        run.SnippetsAdded.Should().BeGreaterThan(0, "initial commit should add snippets");
        run.SnippetsDeleted.Should().Be(0, "initial commit should not delete any snippets");
        run.Status.Should().Be("completed");

        // Process commit 2 (main.py modified, README skipped)
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);

        var secondRun = _context.IndexingRuns
            .Where(r => r.RepositoryId == _testRepo.Id)
            .OrderByDescending(r => r.CompletedAt)
            .First();

        secondRun.FilesSkipped.Should().BeGreaterThan(0,
            "README.md should be skipped since its blob SHA is unchanged");
    }

    // ---------------------------------------------------------------
    // Test 16: FakeGitService returns correct files per commit
    // ---------------------------------------------------------------

    [Fact]
    public async Task FakeGitService_ReturnsCorrectFiles_PerCommit()
    {
        var commit1Files = await _gitService.ListFilesAsync("/any", FakeGitService.Commit1Sha);
        commit1Files.Should().HaveCount(2);
        commit1Files.Should().Contain(f => f.Path == "README.md");
        commit1Files.Should().Contain(f => f.Path == "src/main.py");

        var commit3Files = await _gitService.ListFilesAsync("/any", FakeGitService.Commit3Sha);
        commit3Files.Should().HaveCount(3);
        commit3Files.Should().Contain(f => f.Path == "src/utils.py");

        var commit4Files = await _gitService.ListFilesAsync("/any", FakeGitService.Commit4Sha);
        commit4Files.Should().HaveCount(2, "commit 4 deletes README.md");
        commit4Files.Should().NotContain(f => f.Path == "README.md");

        var commit5Files = await _gitService.ListFilesAsync("/any", FakeGitService.Commit5Sha);
        commit5Files.Should().HaveCount(2, "commit 5 is same as commit 4");
    }

    // ---------------------------------------------------------------
    // Test 17: FakeGitService returns correct file content
    // ---------------------------------------------------------------

    [Fact]
    public async Task FakeGitService_ReturnsCorrectContent()
    {
        var mainV1 = await _gitService.ReadFileAsync("/any", FakeGitService.Commit1Sha, "src/main.py");
        mainV1.Should().Contain("Hello, World!");
        mainV1.Should().NotContain("Version 2");

        var mainV2 = await _gitService.ReadFileAsync("/any", FakeGitService.Commit2Sha, "src/main.py");
        mainV2.Should().Contain("Version 2");

        // Deleted file returns null
        var readmeC4 = await _gitService.ReadFileAsync("/any", FakeGitService.Commit4Sha, "README.md");
        readmeC4.Should().BeNull("README.md was deleted in commit 4");
    }

    // ---------------------------------------------------------------
    // Test 18: Enrichments have correct file paths and languages
    // ---------------------------------------------------------------

    [Fact]
    public async Task Enrichments_HaveCorrectFilePathsAndLanguages()
    {
        await RunExtractAtCommit(FakeGitService.Commit3Sha);

        var enrichments = GetChunkEnrichments();
        enrichments.Should().NotBeEmpty();

        // Python files should have language "python"
        var pythonChunks = enrichments.Where(e => e.FilePath != null && e.FilePath.EndsWith(".py")).ToList();
        pythonChunks.Should().NotBeEmpty();
        pythonChunks.Should().AllSatisfy(e =>
            e.Language.Should().Be("python"));

        // Markdown files should have language "markdown"
        var markdownChunks = enrichments.Where(e => e.FilePath == "README.md").ToList();
        markdownChunks.Should().NotBeEmpty();
        markdownChunks.Should().AllSatisfy(e =>
            e.Language.Should().Be("markdown"));
    }

    // ---------------------------------------------------------------
    // Test 19: FakeGitService branches and tags
    // ---------------------------------------------------------------

    [Fact]
    public async Task FakeGitService_BranchesAndTags()
    {
        var branches = await _gitService.GetBranchesAsync("/any");
        branches.Should().ContainSingle();
        branches[0].Name.Should().Be("main");
        branches[0].IsDefault.Should().BeTrue();

        var tags = await _gitService.GetTagsAsync("/any");
        tags.Should().ContainSingle();
        tags[0].Name.Should().Be("v1.0");
        tags[0].CommitSha.Should().Be(FakeGitService.Commit1Sha);
    }

    // ---------------------------------------------------------------
    // Test 20: FakeGitService resolves refs correctly
    // ---------------------------------------------------------------

    [Fact]
    public async Task FakeGitService_ResolvesRefs()
    {
        var mainSha = await _gitService.ResolveRefAsync("/any", "main");
        mainSha.Should().Be(FakeGitService.Commit5Sha);

        var headSha = await _gitService.ResolveRefAsync("/any", "HEAD");
        headSha.Should().Be(FakeGitService.Commit5Sha);

        var unknownRef = await _gitService.ResolveRefAsync("/any", "nonexistent-branch");
        unknownRef.Should().BeNull();

        var treeHash = await _gitService.GetTreeHashAsync("/any", FakeGitService.Commit1Sha);
        treeHash.Should().NotBeNull();
    }

    // ---------------------------------------------------------------
    // Test 21: Enrichment content is non-empty and meaningful
    // ---------------------------------------------------------------

    [Fact]
    public async Task Enrichments_ContentIsNonEmptyAndMeaningful()
    {
        await RunExtractAtCommit(FakeGitService.Commit1Sha);

        var enrichments = GetChunkEnrichments();
        enrichments.Should().NotBeEmpty();

        enrichments.Should().AllSatisfy(e =>
        {
            e.Content.Should().NotBeNullOrWhiteSpace("enrichment content must not be empty");
            e.Content.Length.Should().BeGreaterThan(10, "enrichment content should be meaningful");
        });
    }

    // ---------------------------------------------------------------
    // Test 22: Full sequential pipeline -- all 5 commits
    // ---------------------------------------------------------------

    [Fact]
    public async Task FullPipeline_AllFiveCommits_ProducesConsistentState()
    {
        // Process all 5 commits sequentially
        await RunExtractAtCommit(FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit2Sha, previousCommitSha: FakeGitService.Commit1Sha);
        await RunExtractAtCommit(FakeGitService.Commit3Sha, previousCommitSha: FakeGitService.Commit2Sha);
        await RunExtractAtCommit(FakeGitService.Commit4Sha, previousCommitSha: FakeGitService.Commit3Sha);
        await RunExtractAtCommit(FakeGitService.Commit5Sha, previousCommitSha: FakeGitService.Commit4Sha);

        var enrichments = GetChunkEnrichments();

        // After commit 5, we should have enrichments only for files that exist at commit 5:
        // src/main.py and src/utils.py (README.md was deleted in commit 4)
        var filePaths = enrichments.Select(e => e.FilePath).Distinct().ToList();
        filePaths.Should().Contain("src/main.py");
        filePaths.Should().Contain("src/utils.py");
        filePaths.Should().NotContain("README.md",
            "README.md was deleted in commit 4 and should have no enrichments");

        // All enrichments should have CommitId (traceability)
        enrichments.Should().AllSatisfy(e =>
            e.CommitId.Should().NotBeNull());

        // 5 indexing runs should be recorded (one per extraction)
        var runs = _context.IndexingRuns.Where(r => r.RepositoryId == _testRepo.Id).ToList();
        runs.Should().HaveCount(5, "one indexing run per commit extraction");
    }
}
