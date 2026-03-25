using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/discover")]
[Produces("application/json")]
[Authorize]
public class DiscoveryController : ControllerBase
{
    private readonly IRepoDiscoveryService _discovery;
    private readonly IRepositoryService _repoService;

    public DiscoveryController(IRepoDiscoveryService discovery, IRepositoryService repoService)
    {
        _discovery = discovery;
        _repoService = repoService;
    }

    /// <summary>Discover repositories in a GitHub organization.</summary>
    [RequirePermission("repository:read")]    [HttpGet("github")]
    [ProducesResponseType(typeof(List<DiscoveredRepo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DiscoverGitHub(
        [FromQuery] string org,
        [FromQuery] string? pat = null,
        [FromQuery] bool excludeArchived = true,
        [FromQuery] bool excludeForks = true,
        CancellationToken ct = default)
    {
        var repos = await _discovery.DiscoverGitHubAsync(org, pat, excludeArchived, excludeForks, ct);
        return Ok(repos);
    }

    /// <summary>Discover repositories in an Azure DevOps organization.</summary>
    [RequirePermission("repository:read")]    [HttpGet("azure-devops")]
    [ProducesResponseType(typeof(List<DiscoveredRepo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DiscoverAzureDevOps(
        [FromQuery] string org,
        [FromQuery] string? project = null,
        [FromQuery] string? pat = null,
        CancellationToken ct = default)
    {
        var repos = await _discovery.DiscoverAzureDevOpsAsync(org, project, pat, ct);
        return Ok(repos);
    }

    /// <summary>Add discovered repositories for indexing.</summary>
    [RequirePermission("repository:write")]    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncDiscoveryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncDiscovered(
        [FromBody] SyncDiscoveryRequest request,
        CancellationToken ct = default)
    {
        var added = new List<RepositoryDto>();
        var skipped = new List<string>();

        foreach (var url in request.RepositoryUrls)
        {
            try
            {
                var repo = await _repoService.AddAsync(
                    new CreateRepositoryRequest { Url = url, PersonalAccessToken = request.Pat }, ct);
                added.Add(repo);
            }
            catch (InvalidOperationException)
            {
                skipped.Add(url); // Already exists
            }
        }

        return Ok(new SyncDiscoveryResponse { Added = added, Skipped = skipped });
    }
}

public class SyncDiscoveryRequest
{
    public required List<string> RepositoryUrls { get; set; }
    public string? Pat { get; set; }
}

public class SyncDiscoveryResponse
{
    public List<RepositoryDto> Added { get; set; } = [];
    public List<string> Skipped { get; set; } = [];
}
