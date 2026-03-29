using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly CodeIndexDbContext _dbContext;

    public CommitsController(
        ICommitRepository commitRepo,
        ICodeRepositoryRepository repoRepo,
        ICommitComparisonService comparisonService,
        CodeIndexDbContext dbContext)
    {
        _commitRepo = commitRepo;
        _repoRepo = repoRepo;
        _comparisonService = comparisonService;
        _dbContext = dbContext;
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

    /// <summary>Get enrichment summary for a specific commit.</summary>
    [RequirePermission("repository:read")]
    [HttpGet("commits/{sha}/summary")]
    [ProducesResponseType(typeof(CommitSummaryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCommitSummary(
        Guid repositoryId,
        string sha,
        CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var commit = await _commitRepo.GetByShaAsync(repositoryId, sha, ct);
        if (commit is null)
            return NotFound(new { error = $"Commit '{sha}' not found." });

        // Count enrichments by subtype for this commit
        var countsBySubtype = await _dbContext.Enrichments
            .Where(e => e.CommitId == commit.Id)
            .GroupBy(e => e.Subtype)
            .Select(g => new { Subtype = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(g => g.Subtype, g => g.Count, ct);

        // Count files indexed at this commit
        var filesIndexed = await _dbContext.RepositoryFiles
            .CountAsync(f => f.CommitId == commit.Id, ct);

        // Count embeddings for enrichments at this commit
        var enrichmentIds = await _dbContext.Enrichments
            .Where(e => e.CommitId == commit.Id)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var embeddingsCount = enrichmentIds.Count > 0
            ? await _dbContext.ContentEmbeddings
                .CountAsync(ce => enrichmentIds.Contains(ce.EnrichmentId), ct)
            : 0;

        var response = new CommitSummaryResponseDto
        {
            Sha = commit.Sha,
            IsIndexed = commit.IsIndexed,
            TotalEnrichments = countsBySubtype.Values.Sum(),
            FilesIndexed = filesIndexed,
            TotalFiles = filesIndexed, // same as indexed for DB commits
            EmbeddingsCount = embeddingsCount,
            CountsBySubtype = countsBySubtype
        };

        return Ok(response);
    }
}
