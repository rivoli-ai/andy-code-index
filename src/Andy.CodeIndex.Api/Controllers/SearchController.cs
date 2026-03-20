using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
[Produces("application/json")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>Hybrid search combining semantic and keyword results via RRF.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SearchResultsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> HybridSearch(
        [FromBody] SearchRequest request,
        CancellationToken ct = default)
    {
        var filter = request.ToFilter();
        var results = await _searchService.HybridSearchAsync(request.Query, filter, request.Limit, ct);
        return Ok(results);
    }

    /// <summary>Semantic similarity search using vector embeddings.</summary>
    [HttpGet("semantic")]
    [ProducesResponseType(typeof(SearchResultsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SemanticSearch(
        [FromQuery] string query,
        [FromQuery] string? language = null,
        [FromQuery] Guid? repositoryId = null,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var filter = new SearchFilter();
        if (language is not null) filter.Languages = [language];
        if (repositoryId.HasValue) filter.RepositoryIds = [repositoryId.Value];

        var results = await _searchService.SemanticSearchAsync(query, filter, limit, ct);
        return Ok(results);
    }

    /// <summary>BM25 keyword search using full-text indexing.</summary>
    [HttpGet("keyword")]
    [ProducesResponseType(typeof(SearchResultsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> KeywordSearch(
        [FromQuery] string keywords,
        [FromQuery] string? language = null,
        [FromQuery] Guid? repositoryId = null,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var filter = new SearchFilter();
        if (language is not null) filter.Languages = [language];
        if (repositoryId.HasValue) filter.RepositoryIds = [repositoryId.Value];

        var results = await _searchService.KeywordSearchAsync(keywords, filter, limit, ct);
        return Ok(results);
    }
}

public class SearchRequest
{
    public required string Query { get; set; }
    public int Limit { get; set; } = 10;
    public List<string>? Languages { get; set; }
    public List<Guid>? RepositoryIds { get; set; }
    public string? CommitSha { get; set; }
    public string? FilePath { get; set; }

    public SearchFilter ToFilter() => new()
    {
        Languages = Languages,
        RepositoryIds = RepositoryIds,
        CommitSha = CommitSha,
        FilePath = FilePath
    };
}
