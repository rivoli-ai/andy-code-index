using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/repositories/{repositoryId:guid}/insights")]
[Produces("application/json")]
[Authorize]
public class InsightsController : ControllerBase
{
    private readonly IEnrichmentGeneratorService _enrichmentService;
    private readonly ITaskQueue _taskQueue;
    private readonly IReportService _reportService;

    private static readonly Dictionary<string, EnrichmentSubtype> LayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["featuremap"] = EnrichmentSubtype.FeatureMap,
        ["architectureanalysis"] = EnrichmentSubtype.ArchitectureAnalysis,
        ["designanalysis"] = EnrichmentSubtype.DesignAnalysis,
        ["implementationanalysis"] = EnrichmentSubtype.ImplementationAnalysis,
        ["dependencyanalysis"] = EnrichmentSubtype.DependencyAnalysis,
        ["testanalysis"] = EnrichmentSubtype.TestAnalysis,
        ["securityanalysis"] = EnrichmentSubtype.SecurityAnalysis,
        ["deploymentanalysis"] = EnrichmentSubtype.DeploymentAnalysis,
        ["operationsanalysis"] = EnrichmentSubtype.OperationsAnalysis,
        ["localsetupguide"] = EnrichmentSubtype.LocalSetupGuide,
        ["techstack"] = EnrichmentSubtype.TechStack,
    };

    private readonly CodeIndexDbContext _dbContext;

    public InsightsController(IEnrichmentGeneratorService enrichmentService, ITaskQueue taskQueue, IReportService reportService, CodeIndexDbContext dbContext)
    {
        _enrichmentService = enrichmentService;
        _taskQueue = taskQueue;
        _reportService = reportService;
        _dbContext = dbContext;
    }

    /// <summary>Get all insight layers for a repository.</summary>
    [HttpGet]
    [RequirePermission("repository:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(Guid repositoryId, CancellationToken ct = default)
    {
        var subtypes = LayerMap.Values.ToArray();
        var results = new Dictionary<string, object?>();

        foreach (var (layerName, subtype) in LayerMap)
        {
            var enrichments = await _enrichmentService.QueryAsync(
                type: EnrichmentType.Insights,
                subtype: subtype,
                repositoryId: repositoryId,
                limit: 1,
                ct: ct);

            results[layerName] = enrichments.Count > 0
                ? new { enrichments[0].Id, enrichments[0].Title, enrichments[0].Content, enrichments[0].Quality, enrichments[0].CreatedAt, enrichments[0].CommitId }
                : null;
        }

        return Ok(new { repositoryId, layers = results });
    }

    /// <summary>Get a specific insight layer for a repository.</summary>
    [HttpGet("{layer}")]
    [RequirePermission("repository:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLayer(Guid repositoryId, string layer, CancellationToken ct = default)
    {
        if (!LayerMap.TryGetValue(layer, out var subtype))
            return NotFound(new { error = $"Unknown insight layer '{layer}'. Valid layers: {string.Join(", ", LayerMap.Keys)}" });

        var enrichments = await _enrichmentService.QueryAsync(
            type: EnrichmentType.Insights,
            subtype: subtype,
            repositoryId: repositoryId,
            limit: 1,
            ct: ct);

        if (enrichments.Count == 0)
            return NotFound(new { error = $"No '{layer}' insight available. Trigger generation first." });

        var e = enrichments[0];
        return Ok(new { e.Id, e.Title, e.Content, e.Quality, e.CreatedAt, layer });
    }

    /// <summary>Trigger insight generation for a repository.</summary>
    [HttpPost("generate")]
    [RequirePermission("repository:execute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Generate(Guid repositoryId, CancellationToken ct = default)
    {
        // Check that the repo has been indexed (has code chunks)
        var chunkCount = await _dbContext.Enrichments
            .CountAsync(e => e.RepositoryId == repositoryId && e.Subtype == EnrichmentSubtype.Chunk, ct);
        if (chunkCount == 0)
            return BadRequest(new { error = "Cannot generate insights: this repository has no indexed code. Click Sync first to index the codebase." });

        var task = await _taskQueue.EnqueueAsync(repositoryId, TaskOperation.CreateInsights, priority: 5, ct: ct);
        return Accepted(new { taskId = task.Id, operation = "CreateInsights", message = "Insight generation queued." });
    }

    /// <summary>Get the insight analysis report for a repository.</summary>
    [HttpGet("~/api/v1/repositories/{repositoryId:guid}/report")]
    [RequirePermission("repository:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(Guid repositoryId, [FromQuery] bool regenerate = false, CancellationToken ct = default)
    {
        try
        {
            var report = await _reportService.GenerateReportAsync(repositoryId, ct, regenerate);
            return Ok(report);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Get the insight analysis report as self-contained HTML.</summary>
    [HttpGet("~/api/v1/repositories/{repositoryId:guid}/report/html")]
    [RequirePermission("repository:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportHtml(Guid repositoryId, CancellationToken ct = default)
    {
        try
        {
            var html = await _reportService.ExportHtmlAsync(repositoryId, ct);
            return Content(html, "text/html");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
