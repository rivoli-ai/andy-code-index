using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/enrichments")]
[Produces("application/json")]
[Authorize]
public class EnrichmentsController : ControllerBase
{
    private readonly IEnrichmentGeneratorService _service;

    public EnrichmentsController(IEnrichmentGeneratorService service)
    {
        _service = service;
    }

    /// <summary>Query enrichments with filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(EnrichmentListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Query(
        [FromQuery] EnrichmentType? type = null,
        [FromQuery] EnrichmentSubtype? subtype = null,
        [FromQuery] Guid? repositoryId = null,
        [FromQuery] Guid? commitId = null,
        [FromQuery] string? language = null,
        [FromQuery] string? filePath = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var results = await _service.QueryAsync(type, subtype, repositoryId, commitId, language, filePath, offset, limit, ct);
        var total = await _service.QueryCountAsync(type, subtype, repositoryId, commitId, language, filePath, ct);

        return Ok(new EnrichmentListResponse
        {
            Results = results,
            TotalCount = total,
            Offset = offset,
            Limit = limit
        });
    }

    /// <summary>Get per-subtype counts, optionally filtered by type and repository.</summary>
    [HttpGet("counts")]
    [ProducesResponseType(typeof(Dictionary<string, int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCounts(
        [FromQuery] EnrichmentType? type = null,
        [FromQuery] Guid? repositoryId = null,
        CancellationToken ct = default)
    {
        var counts = await _service.GetCountsBySubtypeAsync(type, repositoryId, ct);
        return Ok(counts);
    }

    /// <summary>Get enrichment by ID with full content.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EnrichmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var enrichment = await _service.GetByIdAsync(id, ct);
        return enrichment is null ? NotFound() : Ok(enrichment);
    }
}

public class EnrichmentListResponse
{
    public List<EnrichmentDto> Results { get; set; } = [];
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
}
