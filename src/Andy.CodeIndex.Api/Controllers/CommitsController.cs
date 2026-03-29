using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/repositories/{repositoryId:guid}")]
[Produces("application/json")]
[Authorize]
public class CommitsController : ControllerBase
{
    private readonly ICommitRepository _commitRepo;
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly ICommitComparisonService _comparisonService;

    public CommitsController(
        ICommitRepository commitRepo,
        ICodeRepositoryRepository repoRepo,
        ICommitComparisonService comparisonService)
    {
        _commitRepo = commitRepo;
        _repoRepo = repoRepo;
        _comparisonService = comparisonService;
    }

    /// <summary>List commits for a repository, ordered by date descending.</summary>
    [RequirePermission("repository:read")]    [HttpGet("commits")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCommits(
        Guid repositoryId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound();

        var commits = await _commitRepo.GetByRepositoryAsync(repositoryId, offset, limit, ct);
        return Ok(commits.Select(c => new
        {
            c.Id,
            c.Sha,
            c.Message,
            c.AuthorName,
            c.AuthorEmail,
            c.CommittedAt,
            c.IsIndexed
        }));
    }

    /// <summary>Get a single commit by SHA.</summary>
    [RequirePermission("repository:read")]    [HttpGet("commits/{sha}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCommit(Guid repositoryId, string sha, CancellationToken ct = default)
    {
        var commit = await _commitRepo.GetByShaAsync(repositoryId, sha, ct);
        if (commit is null) return NotFound();

        return Ok(new
        {
            commit.Id,
            commit.Sha,
            commit.Message,
            commit.AuthorName,
            commit.AuthorEmail,
            commit.CommittedAt,
            commit.IsIndexed
        });
    }

    /// <summary>Compare enrichments between two commits.</summary>
    [RequirePermission("repository:read")]
    [HttpGet("commits/compare")]
    [ProducesResponseType(typeof(CommitComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompareCommits(
        Guid repositoryId,
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Both 'from' and 'to' commit SHAs are required." });

        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound();

        var result = await _comparisonService.CompareAsync(repositoryId, from, to, ct);
        if (result is null)
            return NotFound(new { error = "One or both commits not found." });

        return Ok(result);
    }
}
