using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.Rbac.Authorization;
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
    [RequirePermission("task:read")]    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var tasks = await _taskRepo.GetAllAsync(ct);
        return Ok(tasks.Select(t => new
        {
            t.Id, t.RepositoryId, t.CommitId,
            operation = t.Operation.ToString(),
            status = t.Status.ToString(),
            t.Progress, t.ProgressMessage,
            t.ErrorMessage, t.ChainId,
            t.ChainStepIndex, t.ChainTotalSteps,
            t.Priority,
            t.CreatedAt, t.StartedAt, t.CompletedAt,
            t.LastHeartbeatAt,
            t.Seq
        }).OrderByDescending(t => t.CreatedAt));
    }

    /// <summary>Get active tasks grouped by repository for pipeline progress.</summary>
    [RequirePermission("task:read")]    [HttpGet("pipelines")]
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

            var totalSteps = chainTasks.FirstOrDefault()?.ChainTotalSteps ?? (chainTasks.Any() ? chainTasks.Count : 0);

            return new
            {
                repositoryId = repoId,
                chainId = activeChainId,
                totalSteps,
                completedSteps = completed,
                currentStep = running?.Operation.ToString(),
                currentStepIndex = running?.ChainStepIndex,
                currentProgress = running?.Progress ?? 0,
                currentProgressMessage = running?.ProgressMessage,
            };
        });

        return Ok(pipelines);
    }

    /// <summary>Get task by ID.</summary>
    [RequirePermission("task:read")]    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        if (task is null) return NotFound();
        return Ok(new
        {
            task.Id, task.RepositoryId, task.CommitId,
            operation = task.Operation.ToString(),
            status = task.Status.ToString(),
            task.Progress, task.ProgressMessage,
            task.ErrorMessage, task.ChainId,
            task.ChainStepIndex, task.ChainTotalSteps,
            task.Priority,
            task.CreatedAt, task.StartedAt, task.CompletedAt,
            task.LastHeartbeatAt,
            task.Seq
        });
    }

    /// <summary>Cancel a pending task.</summary>
    [RequirePermission("task:read")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelPending(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        if (task is null) return NotFound();
        if (task.Status != IndexingTaskStatus.Pending)
            return Conflict(new { error = $"Task is {task.Status}, not Pending. Use POST /api/v1/queue/{{taskId}}/cancel to force-cancel." });

        await _taskRepo.UpdateStatusAsync(id, IndexingTaskStatus.Cancelled, "Cancelled by user", ct);
        return Ok(new { message = "Task cancelled." });
    }

    /// <summary>Force-cancel a task (works on Pending and Running).</summary>
    [RequirePermission("task:read")]
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ForceCancel(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        if (task is null) return NotFound();
        if (task.Status is IndexingTaskStatus.Completed or IndexingTaskStatus.Failed or IndexingTaskStatus.Cancelled)
            return Conflict(new { error = $"Task is already {task.Status} and cannot be cancelled." });

        await _taskRepo.UpdateStatusAsync(id, IndexingTaskStatus.Cancelled, "Force-cancelled by user", ct);
        return Ok(new { message = "Task force-cancelled." });
    }
}
