using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// Verifies repository stats are computed correctly and consistently across the
/// single-repo (<see cref="RepositoryService.GetDetailsByIdAsync"/>) and list
/// (<see cref="RepositoryService.ListAsync"/>) endpoints — including CommitCount
/// and FileCount, which the list endpoint previously left at zero.
/// </summary>
public class RepositoryStatsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CodeIndexDbContext _context;
    private readonly RepositoryService _service;
    private readonly Guid _repoId = Guid.NewGuid();

    public RepositoryStatsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CodeIndexDbContext(options);
        SqliteDatabaseInitializer.Initialize(_context);

        _service = new RepositoryService(
            new CodeRepositoryRepository(_context),
            new CommitRepository(_context),
            new EnrichmentRepository(_context),
            new IndexingTaskRepository(_context),
            _context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Stats_AreComputed_AndConsistentAcrossListAndDetail()
    {
        await SeedAsync(commits: 3, filesPerCommit: 2, enrichments: 5);

        var detail = await _service.GetDetailsByIdAsync(_repoId);
        var list = await _service.ListAsync();
        var listed = Assert.Single(list);

        Assert.NotNull(detail!.Stats);
        Assert.NotNull(listed.Stats);

        // The actual data is reported, not zero.
        Assert.Equal(3, detail.Stats!.CommitCount);
        Assert.Equal(6, detail.Stats.FileCount); // 3 commits * 2 files
        Assert.Equal(5, detail.Stats.EnrichmentCount);

        // Both endpoints agree.
        Assert.Equal(detail.Stats.CommitCount, listed.Stats!.CommitCount);
        Assert.Equal(detail.Stats.FileCount, listed.Stats.FileCount);
        Assert.Equal(detail.Stats.EnrichmentCount, listed.Stats.EnrichmentCount);
    }

    private async Task SeedAsync(int commits, int filesPerCommit, int enrichments)
    {
        _context.Repositories.Add(new Repository
        {
            Id = _repoId,
            Name = "demo",
            Url = "https://example.test/demo",
            Status = "indexed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        for (var i = 0; i < commits; i++)
        {
            var commitId = Guid.NewGuid();
            _context.Commits.Add(new Commit
            {
                Id = commitId,
                RepositoryId = _repoId,
                Sha = $"sha{i:D4}",
                Message = $"commit {i}",
                CommittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            for (var f = 0; f < filesPerCommit; f++)
            {
                _context.Set<RepositoryFile>().Add(new RepositoryFile
                {
                    Id = Guid.NewGuid(),
                    CommitId = commitId,
                    Path = $"src/file{i}_{f}.cs",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        for (var i = 0; i < enrichments; i++)
        {
            _context.Enrichments.Add(new Enrichment
            {
                Id = Guid.NewGuid(),
                RepositoryId = _repoId,
                Type = EnrichmentType.Development,
                Subtype = EnrichmentSubtype.Chunk,
                Content = $"chunk {i}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }
}
