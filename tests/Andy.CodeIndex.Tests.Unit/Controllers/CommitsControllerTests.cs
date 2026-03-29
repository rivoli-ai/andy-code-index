using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Controllers;

public class CommitsControllerTests
{
    private readonly Mock<ICommitRepository> _commitRepoMock = new();
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<ICommitComparisonService> _comparisonServiceMock = new();
    private readonly CodeIndexDbContext _dbContext;
    private readonly CommitsController _controller;
    private readonly Guid _repoId = Guid.NewGuid();

    public CommitsControllerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _controller = new CommitsController(_commitRepoMock.Object, _repoRepoMock.Object, _comparisonServiceMock.Object, _dbContext);

        _repoRepoMock.Setup(r => r.GetByIdAsync(_repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Repository
            {
                Id = _repoId, Name = "test", Url = "https://a.com",
                Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
    }

    [Fact]
    public async Task ListCommits_ExistingRepo_ReturnsOk()
    {
        _commitRepoMock.Setup(r => r.GetByRepositoryAsync(_repoId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Commit { Id = Guid.NewGuid(), RepositoryId = _repoId, Sha = "abc123",
                    Message = "Initial commit", CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow }
            ]);

        var result = await _controller.ListCommits(_repoId);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ListCommits_NonExistentRepo_Returns404()
    {
        _repoRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Repository?)null);

        var result = await _controller.ListCommits(Guid.NewGuid());
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetCommit_ExistingSha_ReturnsOk()
    {
        _commitRepoMock.Setup(r => r.GetByShaAsync(_repoId, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Commit
            {
                Id = Guid.NewGuid(), RepositoryId = _repoId, Sha = "abc123",
                Message = "test", CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });

        var result = await _controller.GetCommit(_repoId, "abc123");
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetCommit_NonExistentSha_Returns404()
    {
        _commitRepoMock.Setup(r => r.GetByShaAsync(_repoId, "notfound", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commit?)null);

        var result = await _controller.GetCommit(_repoId, "notfound");
        result.Should().BeOfType<NotFoundResult>();
    }
}
