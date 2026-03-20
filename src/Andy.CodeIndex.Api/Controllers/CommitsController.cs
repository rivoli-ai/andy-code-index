using Andy.CodeIndex.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/repositories/{repositoryId:guid}")]
[Produces("application/json")]
public class CommitsController : ControllerBase
{
    private readonly ICommitRepository _commitRepo;
    private readonly ICodeRepositoryRepository _repoRepo;

    public CommitsController(ICommitRepository commitRepo, ICodeRepositoryRepository repoRepo)
    {
        _commitRepo = commitRepo;
        _repoRepo = repoRepo;
    }

    /// <summary>List commits for a repository, ordered by date descending.</summary>
    [HttpGet("commits")]
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
    [HttpGet("commits/{sha}")]
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
}
