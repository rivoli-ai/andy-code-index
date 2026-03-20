using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class EnrichmentGeneratorServiceTests
{
    private readonly Mock<IEnrichmentRepository> _enrichmentRepoMock = new();
    private readonly IEnrichmentGeneratorService _service;

    public EnrichmentGeneratorServiceTests()
    {
        _service = new EnrichmentGeneratorService(_enrichmentRepoMock.Object);
    }

    [Fact]
    public async Task QueryAsync_ReturnsMappedDtos()
    {
        _enrichmentRepoMock.Setup(r => r.QueryAsync(
            EnrichmentType.Development, null, null, null, null, null, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Enrichment
                {
                    Id = Guid.NewGuid(), RepositoryId = Guid.NewGuid(),
                    Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk,
                    Content = "test", CreatedAt = DateTime.UtcNow
                }
            ]);

        var result = await _service.QueryAsync(type: EnrichmentType.Development);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(EnrichmentType.Development);
        result[0].Content.Should().Be("test");
    }

    [Fact]
    public async Task QueryCountAsync_ReturnsCount()
    {
        _enrichmentRepoMock.Setup(r => r.QueryCountAsync(
            EnrichmentType.Usage, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        var result = await _service.QueryCountAsync(type: EnrichmentType.Usage);
        result.Should().Be(42);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsDto()
    {
        var id = Guid.NewGuid();
        _enrichmentRepoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Enrichment
            {
                Id = id, RepositoryId = Guid.NewGuid(),
                Type = EnrichmentType.Architecture, Subtype = EnrichmentSubtype.Physical,
                Content = "architecture docs", Title = "System Overview",
                CreatedAt = DateTime.UtcNow
            });

        var result = await _service.GetByIdAsync(id);
        result.Should().NotBeNull();
        result!.Title.Should().Be("System Overview");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        _enrichmentRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enrichment?)null);

        var result = await _service.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteByRepositoryAndTypeAsync_DelegatesToRepo()
    {
        var repoId = Guid.NewGuid();
        await _service.DeleteByRepositoryAndTypeAsync(repoId, EnrichmentType.Development);

        _enrichmentRepoMock.Verify(r => r.DeleteByRepositoryAndTypeAsync(
            repoId, EnrichmentType.Development, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
