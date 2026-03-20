using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Repositories;

public class CommitRepositoryTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly CommitRepository _repo;
    private readonly Repository _testRepo;

    public CommitRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repo = new CommitRepository(_context);
        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private Commit CreateCommit(string sha, bool isIndexed = false, DateTime? committedAt = null)
    {
        return new Commit
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id, Sha = sha,
            Message = $"Commit {sha}", CommittedAt = committedAt ?? DateTime.UtcNow,
            IsIndexed = isIndexed, CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetByShaAsync_ExistingSha_ReturnsCommit()
    {
        await _repo.AddAsync(CreateCommit("abc123"));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByShaAsync(_testRepo.Id, "abc123");
        result.Should().NotBeNull();
        result!.Sha.Should().Be("abc123");
    }

    [Fact]
    public async Task GetByShaAsync_WrongRepository_ReturnsNull()
    {
        await _repo.AddAsync(CreateCommit("abc123"));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByShaAsync(Guid.NewGuid(), "abc123");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByRepositoryAsync_ReturnsOrderedByCommittedAtDesc()
    {
        var now = DateTime.UtcNow;
        await _repo.AddAsync(CreateCommit("oldest", committedAt: now.AddHours(-2)));
        await _repo.AddAsync(CreateCommit("newest", committedAt: now));
        await _repo.AddAsync(CreateCommit("middle", committedAt: now.AddHours(-1)));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByRepositoryAsync(_testRepo.Id);
        result.Should().HaveCount(3);
        result[0].Sha.Should().Be("newest");
        result[1].Sha.Should().Be("middle");
        result[2].Sha.Should().Be("oldest");
    }

    [Fact]
    public async Task GetByRepositoryAsync_RespectsOffsetAndLimit()
    {
        for (int i = 0; i < 5; i++)
            await _repo.AddAsync(CreateCommit($"sha{i}", committedAt: DateTime.UtcNow.AddMinutes(i)));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByRepositoryAsync(_testRepo.Id, offset: 1, limit: 2);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLatestIndexedAsync_ReturnsLatestIndexedCommit()
    {
        var now = DateTime.UtcNow;
        await _repo.AddAsync(CreateCommit("old-indexed", isIndexed: true, committedAt: now.AddHours(-2)));
        await _repo.AddAsync(CreateCommit("new-indexed", isIndexed: true, committedAt: now));
        await _repo.AddAsync(CreateCommit("not-indexed", isIndexed: false, committedAt: now.AddHours(1)));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetLatestIndexedAsync(_testRepo.Id);
        result.Should().NotBeNull();
        result!.Sha.Should().Be("new-indexed");
    }

    [Fact]
    public async Task GetLatestIndexedAsync_NoIndexedCommits_ReturnsNull()
    {
        await _repo.AddAsync(CreateCommit("not-indexed", isIndexed: false));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetLatestIndexedAsync(_testRepo.Id);
        result.Should().BeNull();
    }
}
