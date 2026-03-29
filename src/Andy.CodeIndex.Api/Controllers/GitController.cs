using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/repositories/{repositoryId:guid}")]
[Produces("application/json")]
[Authorize]
public class GitController : ControllerBase
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly IGitService _gitService;
    private readonly ICommitRepository _commitRepo;
    private readonly CodeIndexDbContext _dbContext;
    private readonly IndexingOptions _options;

    public GitController(
        ICodeRepositoryRepository repoRepo,
        IGitService gitService,
        ICommitRepository commitRepo,
        CodeIndexDbContext dbContext,
        IOptions<IndexingOptions> options)
    {
        _repoRepo = repoRepo;
        _gitService = gitService;
        _commitRepo = commitRepo;
        _dbContext = dbContext;
        _options = options.Value;
    }

    /// <summary>Cursor-paginated commit log from live git with enrichment counts.</summary>
    [RequirePermission("repository:read")]
    [HttpGet("git/log")]
    [ProducesResponseType(typeof(GitLogResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GitLog(
        Guid repositoryId,
        [FromQuery(Name = "ref")] string? gitRef = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? before = null,
        CancellationToken ct = default)
    {
        if (limit < 1 || limit > 500)
            return BadRequest(new { error = "Limit must be between 1 and 500." });

        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
            return Conflict(new { error = "Repository not cloned yet." });

        var effectiveRef = gitRef ?? repo.DefaultBranch ?? "HEAD";

        // Validate ref
        var resolvedSha = await _gitService.ResolveRefAsync(cloneDir, effectiveRef, ct);
        if (resolvedSha is null)
            return NotFound(new { error = $"Ref '{effectiveRef}' not found." });

        // Validate before cursor if provided
        if (before is not null)
        {
            var beforeResolved = await _gitService.ResolveRefAsync(cloneDir, before, ct);
            if (beforeResolved is null)
                return BadRequest(new { error = $"Cursor SHA '{before}' not found." });
        }

        List<GitCommitInfo> commits;
        try
        {
            commits = await _gitService.GetCommitsAsync(cloneDir, effectiveRef, limit + 1, before, ct);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        var hasMore = commits.Count > limit;
        if (hasMore)
            commits = commits.Take(limit).ToList();

        // Batch query: get enrichment counts and indexed status for these SHAs
        var shas = commits.Select(c => c.Sha).ToList();
        var indexedCommits = await _dbContext.Commits
            .Where(c => c.RepositoryId == repositoryId && shas.Contains(c.Sha))
            .Select(c => new { c.Sha, c.IsIndexed, c.Id })
            .ToListAsync(ct);

        var commitIdsBySha = indexedCommits.ToDictionary(c => c.Sha, c => c.Id);
        var indexedShas = indexedCommits.Where(c => c.IsIndexed).Select(c => c.Sha).ToHashSet();

        // Batch enrichment counts by commit ID
        var commitIds = commitIdsBySha.Values.ToList();
        var enrichmentCounts = commitIds.Count > 0
            ? await _dbContext.Enrichments
                .Where(e => e.CommitId.HasValue && commitIds.Contains(e.CommitId.Value))
                .GroupBy(e => e.CommitId!.Value)
                .Select(g => new { CommitId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.CommitId, g => g.Count, ct)
            : new Dictionary<Guid, int>();

        var response = new GitLogResponseDto
        {
            HasMore = hasMore,
            NextCursor = hasMore ? commits.Last().Sha : null,
            Commits = commits.Select(c =>
            {
                commitIdsBySha.TryGetValue(c.Sha, out var commitId);
                enrichmentCounts.TryGetValue(commitId, out var enrichCount);

                return new GitLogCommitDto
                {
                    Sha = c.Sha,
                    AbbreviatedSha = c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha,
                    Message = c.Message,
                    AuthorName = c.AuthorName,
                    AuthorEmail = c.AuthorEmail,
                    CommittedAt = c.CommittedAt,
                    ParentShas = c.ParentShas,
                    IsIndexed = indexedShas.Contains(c.Sha),
                    EnrichmentCount = enrichCount
                };
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>List branches and tags.</summary>
    [RequirePermission("repository:read")]
    [HttpGet("git/refs")]
    [ProducesResponseType(typeof(GitRefsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GitRefs(
        Guid repositoryId,
        CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
            return Conflict(new { error = "Repository not cloned yet." });

        var branches = await _gitService.GetBranchesAsync(cloneDir, ct);
        var tags = await _gitService.GetTagsAsync(cloneDir, ct);
        var head = await _gitService.GetHeadRefAsync(cloneDir, ct);

        var response = new GitRefsResponseDto
        {
            Head = head,
            Branches = branches.Select(b => new GitRefBranchDto
            {
                Name = b.Name,
                Sha = b.HeadCommitSha,
                IsDefault = b.IsDefault
            }).ToList(),
            Tags = tags.Select(t => new GitRefTagDto
            {
                Name = t.Name,
                Sha = t.CommitSha
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>File tree at a specific ref.</summary>
    [RequirePermission("repository:read")]
    [HttpGet("git/tree/{*gitRef}")]
    [ProducesResponseType(typeof(GitTreeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GitTree(
        Guid repositoryId,
        string gitRef,
        [FromQuery] string? path = null,
        [FromQuery] bool recursive = false,
        CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
            return Conflict(new { error = "Repository not cloned yet." });

        var resolvedSha = await _gitService.ResolveRefAsync(cloneDir, gitRef, ct);
        if (resolvedSha is null)
            return NotFound(new { error = $"Ref '{gitRef}' not found." });

        List<GitTreeEntry> entries;
        try
        {
            entries = await _gitService.ListTreeAsync(cloneDir, gitRef, path, recursive, ct);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = $"Path '{path}' not found at ref '{gitRef}'." });
        }

        // Get enrichment file paths for this commit to mark enrichment status
        var dbCommit = await _commitRepo.GetByShaAsync(repositoryId, resolvedSha, ct);
        var enrichedFilePaths = new HashSet<string>();

        if (dbCommit is not null)
        {
            enrichedFilePaths = (await _dbContext.Enrichments
                .Where(e => e.CommitId == dbCommit.Id && e.FilePath != null)
                .Select(e => e.FilePath!)
                .Distinct()
                .ToListAsync(ct))
                .ToHashSet();
        }

        var response = new GitTreeResponseDto
        {
            Ref = gitRef,
            Path = path,
            Recursive = recursive,
            Entries = entries.Select(e => new GitTreeEntryDto
            {
                Path = e.Path,
                Name = e.Name,
                Type = e.Type,
                Hash = e.Hash,
                Size = e.Size,
                Language = e.Language,
                HasEnrichments = e.Type == "blob" && enrichedFilePaths.Contains(e.Path)
            }).ToList()
        };

        return Ok(response);
    }
}
