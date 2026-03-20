using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Pgvector;

namespace Andy.CodeIndex.Tests.Unit.Data;

public class CodeIndexDbContextTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;

    public CodeIndexDbContextTests()
    {
        _context = TestDbContextFactory.Create();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CanCreateAndRetrieveRepository()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "andy-docs",
            Url = "https://github.com/rivoli-ai/andy-docs",
            Provider = GitProvider.GitHub,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Repositories.FindAsync(repo.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("andy-docs");
        retrieved.Url.Should().Be("https://github.com/rivoli-ai/andy-docs");
        retrieved.Provider.Should().Be(GitProvider.GitHub);
    }

    [Fact]
    public async Task CanCreateCommitWithRepository()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var commit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Sha = "abc1234567890abcdef1234567890abcdef1234",
            Message = "Initial commit",
            AuthorName = "Test User",
            AuthorEmail = "test@example.com",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        _context.Commits.Add(commit);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Commits.FindAsync(commit.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Sha.Should().Be("abc1234567890abcdef1234567890abcdef1234");
        retrieved.RepositoryId.Should().Be(repo.Id);
    }

    [Fact]
    public async Task CanCreateBranchAndTag()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo2",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Name = "main",
            HeadCommitSha = "abc123",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Name = "v1.0.0",
            CommitSha = "abc123",
            CreatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        _context.Branches.Add(branch);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var branches = _context.Branches.Where(b => b.RepositoryId == repo.Id).ToList();
        branches.Should().HaveCount(1);
        branches[0].Name.Should().Be("main");
        branches[0].IsDefault.Should().BeTrue();

        var tags = _context.Tags.Where(t => t.RepositoryId == repo.Id).ToList();
        tags.Should().HaveCount(1);
        tags[0].Name.Should().Be("v1.0.0");
    }

    [Fact]
    public async Task CanCreateRepositoryFile()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo3",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var commit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Sha = "def456",
            Message = "Add file",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var file = new RepositoryFile
        {
            Id = Guid.NewGuid(),
            CommitId = commit.Id,
            Path = "src/Program.cs",
            Language = "csharp",
            Size = 1024,
            Hash = "sha256hash",
            CreatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        _context.Commits.Add(commit);
        _context.RepositoryFiles.Add(file);
        await _context.SaveChangesAsync();

        var retrieved = await _context.RepositoryFiles.FindAsync(file.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Path.Should().Be("src/Program.cs");
        retrieved.Language.Should().Be("csharp");
        retrieved.Size.Should().Be(1024);
    }

    [Fact]
    public async Task CanCreateEnrichment()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo4",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var enrichment = new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = "public class Program { static void Main() { } }",
            FilePath = "src/Program.cs",
            StartLine = 1,
            EndLine = 5,
            Language = "csharp",
            CreatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        _context.Enrichments.Add(enrichment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Enrichments.FindAsync(enrichment.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Type.Should().Be(EnrichmentType.Development);
        retrieved.Subtype.Should().Be(EnrichmentSubtype.Chunk);
        retrieved.Content.Should().Contain("Program");
    }

    [Fact]
    public async Task CanCreateIndexingTask()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo5",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var chainId = Guid.NewGuid();
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Operation = TaskOperation.CloneRepository,
            Status = IndexingTaskStatus.Pending,
            Progress = 0,
            ChainId = chainId,
            Priority = 10,
            CreatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        _context.IndexingTasks.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.IndexingTasks.FindAsync(task.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Operation.Should().Be(TaskOperation.CloneRepository);
        retrieved.Status.Should().Be(IndexingTaskStatus.Pending);
        retrieved.ChainId.Should().Be(chainId);
        retrieved.Priority.Should().Be(10);
    }

    [Fact]
    public async Task CanCreateChunkLineRange()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo6",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var enrichment = new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = "some code chunk",
            CreatedAt = DateTime.UtcNow
        };

        var lineRange = new ChunkLineRange
        {
            Id = Guid.NewGuid(),
            EnrichmentId = enrichment.Id,
            StartLine = 10,
            EndLine = 25
        };

        _context.Repositories.Add(repo);
        _context.Enrichments.Add(enrichment);
        _context.ChunkLineRanges.Add(lineRange);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ChunkLineRanges.FindAsync(lineRange.Id);
        retrieved.Should().NotBeNull();
        retrieved!.StartLine.Should().Be(10);
        retrieved.EndLine.Should().Be(25);
    }

    [Fact]
    public async Task CascadeDeleteRemovesRelatedEntities()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "cascade-test",
            Url = "https://github.com/test/cascade",
            Provider = GitProvider.GitHub,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var commit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Sha = "cascade123",
            Message = "Test cascade",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Name = "main",
            CreatedAt = DateTime.UtcNow
        };

        var enrichment = new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            CommitId = commit.Id,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = "test content",
            CreatedAt = DateTime.UtcNow
        };

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Operation = TaskOperation.ScanCommit,
            Status = IndexingTaskStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };

        _context.Repositories.Add(repo);
        _context.Commits.Add(commit);
        _context.Branches.Add(branch);
        _context.Enrichments.Add(enrichment);
        _context.IndexingTasks.Add(task);
        await _context.SaveChangesAsync();

        // Delete the repository
        _context.Repositories.Remove(repo);
        await _context.SaveChangesAsync();

        // All related entities should be cascade-deleted
        (await _context.Commits.FindAsync(commit.Id)).Should().BeNull();
        (await _context.Branches.FindAsync(branch.Id)).Should().BeNull();
        (await _context.Enrichments.FindAsync(enrichment.Id)).Should().BeNull();
        (await _context.IndexingTasks.FindAsync(task.Id)).Should().BeNull();
    }

    [Fact]
    public async Task AllEnumValuesCanBeStored()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "enum-test",
            Url = "https://github.com/test/enums",
            Provider = GitProvider.AzureDevOps,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(repo);

        // Test all TaskOperation values
        foreach (var operation in Enum.GetValues<TaskOperation>())
        {
            _context.IndexingTasks.Add(new IndexingTask
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Operation = operation,
                Status = IndexingTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Test all EnrichmentType/Subtype combinations
        foreach (var type in Enum.GetValues<EnrichmentType>())
        {
            _context.Enrichments.Add(new Enrichment
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Type = type,
                Subtype = EnrichmentSubtype.Chunk,
                Content = $"Content for {type}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var tasks = _context.IndexingTasks.Where(t => t.RepositoryId == repo.Id).ToList();
        tasks.Should().HaveCount(Enum.GetValues<TaskOperation>().Length);

        var enrichments = _context.Enrichments.Where(e => e.RepositoryId == repo.Id).ToList();
        enrichments.Should().HaveCount(Enum.GetValues<EnrichmentType>().Length);
    }

    [Fact]
    public async Task AllGitProvidersCanBeStored()
    {
        foreach (var provider in Enum.GetValues<GitProvider>())
        {
            _context.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(),
                Name = $"repo-{provider}",
                Url = $"https://example.com/{provider}",
                Provider = provider,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var repos = _context.Repositories.ToList();
        repos.Should().HaveCount(Enum.GetValues<GitProvider>().Length);
        repos.Select(r => r.Provider).Should().BeEquivalentTo(Enum.GetValues<GitProvider>());
    }
}
