using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
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
            t.Id, t.RepositoryId, t.CommitId,
            operation = t.Operation.ToString(),
            status = t.Status.ToString(),
            t.Progress, t.ErrorMessage, t.ChainId, t.Priority,
            t.CreatedAt, t.StartedAt, t.CompletedAt
        }).OrderByDescending(t => t.CreatedAt));
    }

    /// <summary>Get active tasks grouped by repository for pipeline progress.</summary>
    [HttpGet("pipelines")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPipelines(CancellationToken ct = default)
    {
        var tasks = await _taskRepo.GetAllAsync(ct);
        var activeRepoIds = tasks
            .Where(t => t.Status is IndexingTaskStatus.Pending or IndexingTaskStatus.Running)
            .Select(t => t.RepositoryId)
            .Distinct();

        var pipelines = activeRepoIds.Select(repoId =>
        {
            var repoTasks = tasks
                .Where(t => t.RepositoryId == repoId)
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            // Find the active chain
            var activeChainId = repoTasks
                .Where(t => t.Status is IndexingTaskStatus.Pending or IndexingTaskStatus.Running)
                .Select(t => t.ChainId)
                .FirstOrDefault();

            var chainTasks = activeChainId.HasValue
                ? repoTasks.Where(t => t.ChainId == activeChainId).OrderBy(t => t.CreatedAt).ToList()
                : new List<IndexingTask>();

            var completed = chainTasks.Count(t => t.Status == IndexingTaskStatus.Completed);
            var running = chainTasks.FirstOrDefault(t => t.Status == IndexingTaskStatus.Running);

            return new
            {
                repositoryId = repoId,
                chainId = activeChainId,
                totalSteps = chainTasks.Any() ? 15 : 0, // Full chain has 15 operations
                completedSteps = completed,
                currentStep = running?.Operation.ToString(),
                currentProgress = running?.Progress ?? 0,
            };
        });

        return Ok(pipelines);
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
