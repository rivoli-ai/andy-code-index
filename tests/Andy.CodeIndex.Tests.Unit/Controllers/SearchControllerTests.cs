using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Controllers;

public class SearchControllerTests
{
    private readonly Mock<ISearchService> _searchMock = new();
    private readonly SearchController _controller;

    public SearchControllerTests()
    {
        _controller = new SearchController(_searchMock.Object);
    }

    [Fact]
    public async Task HybridSearch_ReturnsOkWithResults()
    {
        _searchMock.Setup(s => s.HybridSearchAsync(It.IsAny<string>(), It.IsAny<SearchFilter>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto
            {
                Results = [new SearchResultItem { EnrichmentId = Guid.NewGuid(), Content = "result", Score = 0.95 }],
                TotalCount = 1,
                SearchMode = "hybrid"
            });

        var result = await _controller.HybridSearch(new SearchRequest { Query = "test" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<SearchResultsDto>().Subject;
        dto.Results.Should().HaveCount(1);
        dto.SearchMode.Should().Be("hybrid");
    }

    [Fact]
    public async Task SemanticSearch_ReturnsOkWithResults()
    {
        _searchMock.Setup(s => s.SemanticSearchAsync(It.IsAny<string>(), It.IsAny<SearchFilter>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto { Results = [], TotalCount = 0, SearchMode = "semantic" });

        var result = await _controller.SemanticSearch("test");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task KeywordSearch_ReturnsOkWithResults()
    {
        _searchMock.Setup(s => s.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<SearchFilter>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto { Results = [], TotalCount = 0, SearchMode = "keyword" });

        var result = await _controller.KeywordSearch("test");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SemanticSearch_WithFilters_PassesFilterToService()
    {
        var repoId = Guid.NewGuid();
        _searchMock.Setup(s => s.SemanticSearchAsync(
            "query",
            It.Is<SearchFilter>(f => f.Languages!.Contains("csharp") && f.RepositoryIds!.Contains(repoId)),
            10,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto());

        await _controller.SemanticSearch("query", language: "csharp", repositoryId: repoId);

        _searchMock.Verify(s => s.SemanticSearchAsync(
            "query",
            It.Is<SearchFilter>(f => f.Languages!.Contains("csharp")),
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
