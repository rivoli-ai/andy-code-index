using Andy.CodeIndex.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/queue")]
[Produces("application/json")]
[Authorize]
public class QueueController : ControllerBase
{
    private readonly IIndexingTaskRepository _taskRepo;

    public QueueController(IIndexingTaskRepository taskRepo)
    {
        _taskRepo = taskRepo;
    }

    /// <summary>Get all tasks.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var tasks = await _taskRepo.GetAllAsync(ct);
        return Ok(tasks.Select(t => new
        {
            t.Id, t.RepositoryId, t.CommitId, t.Operation, t.Status,
            t.Progress, t.ErrorMessage, t.ChainId, t.Priority,
            t.CreatedAt, t.StartedAt, t.CompletedAt
        }).OrderByDescending(t => t.CreatedAt));
    }

    /// <summary>Get task by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        return task is null ? NotFound() : Ok(task);
    }
}
