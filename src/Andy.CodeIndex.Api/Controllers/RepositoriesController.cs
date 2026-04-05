using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Authorize]
public class RepositoriesController : ControllerBase
{
    private readonly IRepositoryService _service;
    private readonly IActivityAnalyticsService _activityService;

    public RepositoriesController(IRepositoryService service, IActivityAnalyticsService activityService)
    {
        _service = service;
        _activityService = activityService;
    }

    /// <summary>List all tracked repositories.</summary>
    [HttpGet]
    [RequirePermission("repository:read")]
    [ProducesResponseType(typeof(List<RepositoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] GitProvider? provider = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var repos = await _service.ListAsync(provider, status, ct);
        return Ok(repos);
    }

    /// <summary>Check if a repository URL is already tracked.</summary>
    [HttpGet("check-url")]
    [RequirePermission("repository:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUrl([FromQuery] string url, CancellationToken ct = default)
    {
        var existing = await _service.FindByUrlAsync(url, ct);
        if (existing is null)
            return Ok(new { tracked = false });
        return Ok(new { tracked = true, existingRepositoryId = existing.Id, name = existing.Name });
    }

    /// <summary>Add a new repository for indexing.</summary>
    [HttpPost]
    [RequirePermission("repository:write")]
    [ProducesResponseType(typeof(RepositoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRepositoryRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(ModelState);

        try
        {
            var repo = await _service.AddAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = repo.Id }, repo);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            var parts = ex.Message.Split('|');
            var message = parts[0];
            var existingId = parts.Length > 1 ? parts[1] : null;
            return Conflict(new { error = message, existingRepositoryId = existingId });
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (UriFormatException)
        {
            return UnprocessableEntity(new { error = "Invalid repository URL format." });
        }
    }

    /// <summary>Get repository details including branches, tags, and stats.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("repository:read")]
    [ProducesResponseType(typeof(RepositoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var repo = await _service.GetDetailsByIdAsync(id, ct);
        return repo is null ? NotFound() : Ok(repo);
    }

    /// <summary>Update repository settings (e.g. sync interval).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("repository:write")]
    [ProducesResponseType(typeof(RepositoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateRepositoryRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var repo = await _service.UpdateAsync(id, request, ct);
            return Ok(repo);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    /// <summary>Delete a repository and all associated data.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("repository:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Delete all enrichments for a repository.</summary>
    [HttpDelete("{id:guid}/enrichments")]
    [RequirePermission("repository:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> WipeEnrichments(Guid id, CancellationToken ct = default)
    {
        try
        {
            await _service.WipeEnrichmentsAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Trigger a manual sync for a repository.</summary>
    [HttpPost("{id:guid}/sync")]
    [RequirePermission("repository:execute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct = default)
    {
        try
        {
            await _service.SyncAsync(id, ct);
            return Accepted();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Get bulk sparkline data for multiple repositories.</summary>
    [HttpGet("analytics/bulk/activity-sparklines")]
    [RequirePermission("repository:read")]
    public async Task<IActionResult> GetBulkSparklines([FromQuery] string repositoryIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryIds))
            return BadRequest(new { error = "repositoryIds parameter is required" });

        var ids = repositoryIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Take(50)
            .ToList();

        if (ids.Count == 0)
            return BadRequest(new { error = "No valid repository IDs provided" });

        var result = await _activityService.GetBulkSparklinesAsync(ids, ct: ct);
        return Ok(result);
    }
}
