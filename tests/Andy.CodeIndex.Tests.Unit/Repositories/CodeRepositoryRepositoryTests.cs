using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Repositories;

public class CodeRepositoryRepositoryTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly CodeRepositoryRepository _repo;

    public CodeRepositoryRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repo = new CodeRepositoryRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private Repository CreateRepo(string name = "test-repo", string url = "https://github.com/test/repo",
        GitProvider provider = GitProvider.GitHub, string status = "pending")
    {
        return new Repository
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            Provider = provider,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetByIdAsync_ExistingRepo_ReturnsRepo()
    {
        var repo = CreateRepo();
        await _repo.AddAsync(repo);
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(repo.Id);
        result.Should().NotBeNull();
        result!.Name.Should().Be("test-repo");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByUrlAsync_ExistingUrl_ReturnsRepo()
    {
        var repo = CreateRepo();
        await _repo.AddAsync(repo);
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByUrlAsync("https://github.com/test/repo");
        result.Should().NotBeNull();
        result!.Id.Should().Be(repo.Id);
    }

    [Fact]
    public async Task GetByUrlAsync_NonExistentUrl_ReturnsNull()
    {
        var result = await _repo.GetByUrlAsync("https://github.com/not/found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByNameAsync_ExistingName_ReturnsRepo()
    {
        var repo = CreateRepo(name: "my-repo");
        await _repo.AddAsync(repo);
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByNameAsync("my-repo");
        result.Should().NotBeNull();
        result!.Id.Should().Be(repo.Id);
    }

    [Fact]
    public async Task GetByStatusAsync_ReturnsMatchingRepos()
    {
        await _repo.AddAsync(CreateRepo(name: "r1", url: "https://a.com/1", status: "indexed"));
        await _repo.AddAsync(CreateRepo(name: "r2", url: "https://a.com/2", status: "pending"));
        await _repo.AddAsync(CreateRepo(name: "r3", url: "https://a.com/3", status: "indexed"));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByStatusAsync("indexed");
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.Status.Should().Be("indexed"));
    }

    [Fact]
    public async Task GetByProviderAsync_ReturnsMatchingRepos()
    {
        await _repo.AddAsync(CreateRepo(name: "r1", url: "https://a.com/1", provider: GitProvider.GitHub));
        await _repo.AddAsync(CreateRepo(name: "r2", url: "https://a.com/2", provider: GitProvider.GitLab));
        await _repo.AddAsync(CreateRepo(name: "r3", url: "https://a.com/3", provider: GitProvider.GitHub));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByProviderAsync(GitProvider.GitHub);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetWithBranchesAndTagsAsync_IncludesRelated()
    {
        var repo = CreateRepo();
        repo.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id, Name = "main",
            IsDefault = true, CreatedAt = DateTime.UtcNow
        });
        repo.Tags.Add(new Tag
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id, Name = "v1.0",
            CommitSha = "abc123", CreatedAt = DateTime.UtcNow
        });
        await _repo.AddAsync(repo);
        await _repo.SaveChangesAsync();

        var result = await _repo.GetWithBranchesAndTagsAsync(repo.Id);
        result.Should().NotBeNull();
        result!.Branches.Should().HaveCount(1);
        result.Tags.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllRepos()
    {
        await _repo.AddAsync(CreateRepo(name: "r1", url: "https://a.com/1"));
        await _repo.AddAsync(CreateRepo(name: "r2", url: "https://a.com/2"));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetAllAsync();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExistsAsync_ExistingPredicate_ReturnsTrue()
    {
        await _repo.AddAsync(CreateRepo());
        await _repo.SaveChangesAsync();

        var result = await _repo.ExistsAsync(r => r.Name == "test-repo");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonMatchingPredicate_ReturnsFalse()
    {
        var result = await _repo.ExistsAsync(r => r.Name == "nonexistent");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_DeletesRepo()
    {
        var repo = CreateRepo();
        await _repo.AddAsync(repo);
        await _repo.SaveChangesAsync();

        _repo.Remove(repo);
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(repo.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Update_ModifiesRepo()
    {
        var repo = CreateRepo();
        await _repo.AddAsync(repo);
        await _repo.SaveChangesAsync();

        repo.Status = "indexed";
        repo.LastSyncedAt = DateTime.UtcNow;
        _repo.Update(repo);
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(repo.Id);
        result!.Status.Should().Be("indexed");
        result.LastSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        await _repo.AddAsync(CreateRepo(name: "r1", url: "https://a.com/1", status: "indexed"));
        await _repo.AddAsync(CreateRepo(name: "r2", url: "https://a.com/2", status: "pending"));
        await _repo.SaveChangesAsync();

        var total = await _repo.CountAsync();
        total.Should().Be(2);

        var indexed = await _repo.CountAsync(r => r.Status == "indexed");
        indexed.Should().Be(1);
    }
}
