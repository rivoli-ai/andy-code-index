using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/repositories/{repositoryId:guid}")]
[Produces("application/json")]
public class FilesController : ControllerBase
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _options;

    public FilesController(
        ICodeRepositoryRepository repoRepo,
        IGitService gitService,
        IOptions<IndexingOptions> options)
    {
        _repoRepo = repoRepo;
        _gitService = gitService;
        _options = options.Value;
    }

    /// <summary>Read file content at a specific ref (branch, tag, or commit SHA).</summary>
    [HttpGet("blob/{gitRef}/{**filePath}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadFile(
        Guid repositoryId, string gitRef, string filePath, CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
            return NotFound(new { error = "Repository not cloned yet." });

        var content = await _gitService.ReadFileAsync(cloneDir, gitRef, filePath, ct);
        if (content is null) return NotFound(new { error = "File not found at specified ref." });

        return Ok(new { path = filePath, gitRef, content, lineCount = content.Split('\n').Length });
    }

    /// <summary>List files matching a glob pattern.</summary>
    [HttpGet("ls")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListFiles(
        Guid repositoryId,
        [FromQuery] string? pattern = null,
        [FromQuery] string? gitRef = null,
        CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
            return NotFound(new { error = "Repository not cloned yet." });

        var commitSha = gitRef ?? repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, pattern, ct);
        return Ok(files);
    }

    /// <summary>Search file contents with a regex pattern.</summary>
    [HttpGet("grep")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Grep(
        Guid repositoryId,
        [FromQuery] string pattern,
        [FromQuery] string? glob = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(repositoryId, ct);
        if (repo is null) return NotFound(new { error = "Repository not found." });

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
            return NotFound(new { error = "Repository not cloned yet." });

        var results = await _gitService.GrepAsync(cloneDir, pattern, glob, limit, ct);
        return Ok(results);
    }
}
