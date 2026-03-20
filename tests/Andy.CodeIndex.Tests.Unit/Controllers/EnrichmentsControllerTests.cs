using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Controllers;

public class EnrichmentsControllerTests
{
    private readonly Mock<IEnrichmentGeneratorService> _serviceMock = new();
    private readonly EnrichmentsController _controller;

    public EnrichmentsControllerTests()
    {
        _controller = new EnrichmentsController(_serviceMock.Object);
    }

    [Fact]
    public async Task Query_ReturnsOkWithPaginatedResults()
    {
        _serviceMock.Setup(s => s.QueryAsync(
            EnrichmentType.Development, null, null, null, null, null, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnrichmentDto { Id = Guid.NewGuid(), Content = "test", Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk }]);
        _serviceMock.Setup(s => s.QueryCountAsync(
            EnrichmentType.Development, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _controller.Query(type: EnrichmentType.Development);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<EnrichmentListResponse>().Subject;
        response.Results.Should().HaveCount(1);
        response.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsOk()
    {
        var id = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentDto { Id = id, Content = "test content" });

        var result = await _controller.GetById(id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<EnrichmentDto>();
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        _serviceMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnrichmentDto?)null);

        var result = await _controller.GetById(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }
}
