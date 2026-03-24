using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
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

    public RepositoriesController(IRepositoryService service)
    {
        _service = service;
    }

    /// <summary>List all tracked repositories.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<RepositoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] GitProvider? provider = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var repos = await _service.ListAsync(provider, status, ct);
        return Ok(repos);
    }

    /// <summary>Add a new repository for indexing.</summary>
    [HttpPost]
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
            return Conflict(new { error = ex.Message });
        }
        catch (UriFormatException)
        {
            return UnprocessableEntity(new { error = "Invalid repository URL format." });
        }
    }

    /// <summary>Get repository details including branches, tags, and stats.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RepositoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var repo = await _service.GetDetailsByIdAsync(id, ct);
        return repo is null ? NotFound() : Ok(repo);
    }

    /// <summary>Delete a repository and all associated data.</summary>
    [HttpDelete("{id:guid}")]
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

    /// <summary>Trigger a manual sync for a repository.</summary>
    [HttpPost("{id:guid}/sync")]
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
}
