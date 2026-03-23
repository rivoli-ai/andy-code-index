using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
public class IndexingController : ControllerBase
{
    private readonly CodeIndexDbContext _context;
    private readonly SyncOptions _syncOptions;

    public IndexingController(CodeIndexDbContext context, IOptions<SyncOptions> syncOptions)
    {
        _context = context;
        _syncOptions = syncOptions.Value;
    }

    /// <summary>Get indexing history for a repository.</summary>
    [HttpGet("repositories/{repositoryId:guid}/history")]
    [ProducesResponseType(typeof(List<IndexingRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid repositoryId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var repo = await _context.Repositories.FindAsync([repositoryId], ct);
        if (repo is null) return NotFound();

        var runs = await _context.IndexingRuns
            .Where(r => r.RepositoryId == repositoryId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .Select(r => new IndexingRunDto
            {
                Id = r.Id,
                RepositoryId = r.RepositoryId,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                DurationSeconds = r.CompletedAt.HasValue
                    ? (r.CompletedAt.Value - r.StartedAt).TotalSeconds
                    : null,
                Status = r.Status,
                SnippetsAdded = r.SnippetsAdded,
                SnippetsUpdated = r.SnippetsUpdated,
                SnippetsDeleted = r.SnippetsDeleted,
                SnippetsUnchanged = r.SnippetsUnchanged,
                ApiDocsGenerated = r.ApiDocsGenerated,
                CommitsScanned = r.CommitsScanned,
                ErrorMessage = r.ErrorMessage
            })
            .ToListAsync(ct);

        return Ok(runs);
    }

    /// <summary>Get periodic sync status and schedule.</summary>
    [HttpGet("sync/status")]
    [ProducesResponseType(typeof(SyncStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncStatus(CancellationToken ct = default)
    {
        var repoCount = await _context.Repositories.CountAsync(ct);

        // Find last sync task completion
        var lastSync = await _context.IndexingTasks
            .Where(t => t.Operation == Domain.Enums.TaskOperation.SyncRepository &&
                        t.Status == Domain.Enums.IndexingTaskStatus.Completed)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => t.CompletedAt)
            .FirstOrDefaultAsync(ct);

        DateTime? nextRun = null;
        if (_syncOptions.Enabled && lastSync.HasValue)
            nextRun = lastSync.Value.AddSeconds(_syncOptions.IntervalSeconds);
        else if (_syncOptions.Enabled)
            nextRun = DateTime.UtcNow.AddSeconds(_syncOptions.IntervalSeconds);

        return Ok(new SyncStatusDto
        {
            Enabled = _syncOptions.Enabled,
            IntervalSeconds = _syncOptions.IntervalSeconds,
            LastRunAt = lastSync,
            NextRunAt = nextRun,
            RepositoriesTracked = repoCount
        });
    }
}
