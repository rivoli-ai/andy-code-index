using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Controllers;

public class RepositoriesControllerTests
{
    private readonly Mock<IRepositoryService> _serviceMock = new();
    private readonly RepositoriesController _controller;

    public RepositoriesControllerTests()
    {
        _controller = new RepositoriesController(_serviceMock.Object);
    }

    // --- List ---

    [Fact]
    public async Task List_ReturnsOkWithRepositories()
    {
        _serviceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RepositoryDto { Id = Guid.NewGuid(), Name = "r1", Url = "https://a.com/1", Status = "indexed" },
                new RepositoryDto { Id = Guid.NewGuid(), Name = "r2", Url = "https://a.com/2", Status = "pending" }
            ]);

        var result = await _controller.List();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var repos = ok.Value.Should().BeAssignableTo<List<RepositoryDto>>().Subject;
        repos.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_WithProviderFilter_PassesFilterToService()
    {
        _serviceMock.Setup(s => s.ListAsync(GitProvider.GitLab, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _controller.List(provider: GitProvider.GitLab);

        _serviceMock.Verify(s => s.ListAsync(GitProvider.GitLab, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_EmptyResult_ReturnsOkWithEmptyList()
    {
        _serviceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _controller.List();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var repos = ok.Value.Should().BeAssignableTo<List<RepositoryDto>>().Subject;
        repos.Should().BeEmpty();
    }

    // --- Create ---

    [Fact]
    public async Task Create_ValidRequest_Returns201Created()
    {
        var repoId = Guid.NewGuid();
        _serviceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryDto
            {
                Id = repoId, Name = "andy-docs", Url = "https://github.com/rivoli-ai/andy-docs",
                Provider = GitProvider.GitHub, Status = "pending"
            });

        var result = await _controller.Create(new CreateRepositoryRequest
        {
            Url = "https://github.com/rivoli-ai/andy-docs"
        });

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        var dto = created.Value.Should().BeOfType<RepositoryDto>().Subject;
        dto.Id.Should().Be(repoId);
        dto.Name.Should().Be("andy-docs");
    }

    [Fact]
    public async Task Create_DuplicateUrl_Returns409Conflict()
    {
        _serviceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Repository with URL 'x' already exists."));

        var result = await _controller.Create(new CreateRepositoryRequest
        {
            Url = "https://github.com/test/repo"
        });

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Create_InvalidUrl_Returns422()
    {
        _serviceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UriFormatException("Invalid URI"));

        var result = await _controller.Create(new CreateRepositoryRequest
        {
            Url = "not-a-url"
        });

        result.Should().BeOfType<UnprocessableEntityObjectResult>();
    }

    // --- GetById ---

    [Fact]
    public async Task GetById_ExistingRepo_ReturnsOk()
    {
        var repoId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetDetailsByIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryDto
            {
                Id = repoId, Name = "test", Url = "https://a.com",
                Status = "indexed",
                Branches = [new BranchDto { Name = "main", HeadCommitSha = "abc", IsDefault = true }],
                Stats = new RepositoryStatsDto { CommitCount = 10, EnrichmentCount = 50 }
            });

        var result = await _controller.GetById(repoId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<RepositoryDto>().Subject;
        dto.Branches.Should().HaveCount(1);
        dto.Stats!.CommitCount.Should().Be(10);
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        _serviceMock.Setup(s => s.GetDetailsByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositoryDto?)null);

        var result = await _controller.GetById(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_ExistingRepo_Returns204()
    {
        var repoId = Guid.NewGuid();
        _serviceMock.Setup(s => s.DeleteAsync(repoId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete(repoId);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        _serviceMock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.Delete(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- Sync ---

    [Fact]
    public async Task Sync_ExistingRepo_Returns202()
    {
        var repoId = Guid.NewGuid();
        _serviceMock.Setup(s => s.SyncAsync(repoId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Sync(repoId);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Sync_NonExistent_Returns404()
    {
        _serviceMock.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.Sync(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }
}
