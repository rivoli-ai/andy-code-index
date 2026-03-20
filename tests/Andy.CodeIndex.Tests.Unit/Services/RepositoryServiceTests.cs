using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class RepositoryServiceTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<ICommitRepository> _commitRepoMock = new();
    private readonly Mock<IEnrichmentRepository> _enrichmentRepoMock = new();
    private readonly Mock<IIndexingTaskRepository> _taskRepoMock = new();
    private readonly RepositoryService _service;

    public RepositoryServiceTests()
    {
        _service = new RepositoryService(
            _repoRepoMock.Object,
            _commitRepoMock.Object,
            _enrichmentRepoMock.Object,
            _taskRepoMock.Object);
    }

    [Fact]
    public async Task AddAsync_ValidUrl_CreatesRepoAndQueuesClone()
    {
        _repoRepoMock.Setup(r => r.GetByUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);
        _repoRepoMock.Setup(r => r.AddAsync(It.IsAny<Repository>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository r, CancellationToken _) => r);

        var result = await _service.AddAsync(new CreateRepositoryRequest
        {
            Url = "https://github.com/rivoli-ai/andy-docs"
        });

        result.Should().NotBeNull();
        result.Name.Should().Be("andy-docs");
        result.Provider.Should().Be(GitProvider.GitHub);
        result.Status.Should().Be("pending");

        _repoRepoMock.Verify(r => r.AddAsync(It.IsAny<Repository>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        _taskRepoMock.Verify(t => t.AddAsync(
            It.Is<IndexingTask>(task => task.Operation == TaskOperation.CloneRepository),
            It.IsAny<CancellationToken>()), Times.Once);
        _taskRepoMock.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_DuplicateUrl_Throws()
    {
        _repoRepoMock.Setup(r => r.GetByUrlAsync("https://github.com/test/repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Repository
            {
                Id = Guid.NewGuid(), Name = "repo", Url = "https://github.com/test/repo",
                Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

        var act = () => _service.AddAsync(new CreateRepositoryRequest { Url = "https://github.com/test/repo" });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task AddAsync_WithPat_StoresPat()
    {
        _repoRepoMock.Setup(r => r.GetByUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);
        _repoRepoMock.Setup(r => r.AddAsync(It.IsAny<Repository>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository r, CancellationToken _) => r);

        await _service.AddAsync(new CreateRepositoryRequest
        {
            Url = "https://github.com/private/repo",
            PersonalAccessToken = "ghp_secret123"
        });

        _repoRepoMock.Verify(r => r.AddAsync(
            It.Is<Repository>(repo => repo.PersonalAccessToken == "ghp_secret123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingRepo_ReturnsDto()
    {
        var repoId = Guid.NewGuid();
        _repoRepoMock.Setup(r => r.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Repository
            {
                Id = repoId, Name = "test", Url = "https://github.com/t/r",
                Provider = GitProvider.GitHub, Status = "indexed",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

        var result = await _service.GetByIdAsync(repoId);
        result.Should().NotBeNull();
        result!.Id.Should().Be(repoId);
        result.Name.Should().Be("test");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var result = await _service.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ExistingRepo_RemovesAndSaves()
    {
        var repoId = Guid.NewGuid();
        var repo = new Repository
        {
            Id = repoId, Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _repoRepoMock.Setup(r => r.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        await _service.DeleteAsync(repoId);

        _repoRepoMock.Verify(r => r.Remove(repo), Times.Once);
        _repoRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_Throws()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var act = () => _service.DeleteAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SyncAsync_ExistingRepo_QueuesSyncTask()
    {
        var repoId = Guid.NewGuid();
        _repoRepoMock.Setup(r => r.GetByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Repository
            {
                Id = repoId, Name = "test", Url = "https://github.com/t/r",
                Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

        await _service.SyncAsync(repoId);

        _taskRepoMock.Verify(t => t.AddAsync(
            It.Is<IndexingTask>(task =>
                task.Operation == TaskOperation.SyncRepository &&
                task.RepositoryId == repoId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_NonExistent_Throws()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var act = () => _service.SyncAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ListAsync_NoFilters_ReturnsAll()
    {
        _repoRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Repository { Id = Guid.NewGuid(), Name = "r1", Url = "https://a.com/1",
                    Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new Repository { Id = Guid.NewGuid(), Name = "r2", Url = "https://a.com/2",
                    Provider = GitProvider.GitLab, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            ]);

        var result = await _service.ListAsync();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_FilterByProvider_CallsProviderMethod()
    {
        _repoRepoMock.Setup(r => r.GetByProviderAsync(GitProvider.GitLab, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _service.ListAsync(provider: GitProvider.GitLab);

        _repoRepoMock.Verify(r => r.GetByProviderAsync(GitProvider.GitLab, It.IsAny<CancellationToken>()), Times.Once);
    }

    // URL parsing tests
    [Theory]
    [InlineData("https://github.com/rivoli-ai/andy-docs", GitProvider.GitHub)]
    [InlineData("https://github.com/rivoli-ai/andy-docs.git", GitProvider.GitHub)]
    [InlineData("https://gitlab.com/group/project", GitProvider.GitLab)]
    [InlineData("https://gitea.example.com/org/repo", GitProvider.Gitea)]
    [InlineData("https://dev.azure.com/org/project/_git/repo", GitProvider.AzureDevOps)]
    [InlineData("https://org.visualstudio.com/project/_git/repo", GitProvider.AzureDevOps)]
    public void ParseProvider_DetectsCorrectProvider(string url, GitProvider expected)
    {
        RepositoryService.ParseProvider(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://github.com/rivoli-ai/andy-docs", "andy-docs")]
    [InlineData("https://github.com/rivoli-ai/andy-docs.git", "andy-docs")]
    [InlineData("https://gitlab.com/group/project", "project")]
    [InlineData("https://dev.azure.com/org/project/_git/myrepo", "myrepo")]
    [InlineData("https://github.com/owner/repo/", "repo")]
    public void ParseName_ExtractsCorrectName(string url, string expected)
    {
        RepositoryService.ParseName(url).Should().Be(expected);
    }
}
