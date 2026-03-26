using System.Text;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CreateQualityDocsHandler : BaseLlmEnrichmentHandler
{
    private readonly IGitService _gitService;
    private readonly IndexingOptions _indexingOptions;

    public override TaskOperation Operation => TaskOperation.CreateQualityDocs;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Quality;
    protected override EnrichmentType Type => EnrichmentType.Development;

    public CreateQualityDocsHandler(
        CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http,
        IGitService gitService, IOptions<IndexingOptions> indexingOptions,
        ILogger<CreateQualityDocsHandler> logger)
        : base(context, resolver, opts, http, logger)
    {
        _gitService = gitService;
        _indexingOptions = indexingOptions.Value;
    }

    public override async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await Context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var (apiKey, model, source) = await ApiKeyResolver.ResolveLlmKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogInformation("Skipping {Operation} for {Name}: no LLM key available", Operation, repo.Name);
            return;
        }

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";

        // Count test files and detect patterns
        var stats = new StringBuilder();
        var allFiles = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);
        var testFiles = allFiles.Where(f =>
            f.Path.Contains("test", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("spec", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Contains("Test", StringComparison.Ordinal)).ToList();
        var totalFiles = allFiles.Count;

        stats.AppendLine($"Total files: {totalFiles}");
        stats.AppendLine($"Test files: {testFiles.Count} ({(totalFiles > 0 ? testFiles.Count * 100 / totalFiles : 0)}%)");
        stats.AppendLine();

        // Detect test frameworks from dependency enrichments
        var depsEnrichment = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Dependencies)
            .FirstOrDefaultAsync(ct);
        if (depsEnrichment is not null)
        {
            stats.AppendLine("Dependencies (for test framework detection):");
            stats.AppendLine(depsEnrichment.Content.Length > 2000 ? depsEnrichment.Content[..2000] : depsEnrichment.Content);
            stats.AppendLine();
        }

        // Look for quality config files
        var qualityPatterns = new[] { ".editorconfig", ".eslintrc*", ".prettierrc*", "tslint.json", "sonar-project.properties", "coverlet*", "jest.config*", "karma.conf*", ".nycrc*", "codecov.yml" };
        var qualityFiles = new StringBuilder();
        foreach (var pattern in qualityPatterns)
        {
            try
            {
                var files = await _gitService.ListFilesAsync(cloneDir, commitSha, pattern, ct);
                foreach (var file in files.Take(2))
                    qualityFiles.AppendLine($"- {file.Path}");
            }
            catch { }
        }

        var chunks = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .Where(e => e.FilePath != null && (
                e.FilePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                e.FilePath.Contains("spec", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.FilePath)
            .Take(20)
            .ToListAsync(ct);

        var prompt = $"""
        Analyze the quality and testing strategy of "{repo.Name}".

        File statistics:
        {stats}

        Quality/config files found:
        {(qualityFiles.Length > 0 ? qualityFiles.ToString() : "None detected.")}

        Test code samples:
        {SummarizeChunks(chunks, 4000)}

        Document:
        1. Test strategy: What types of tests exist (unit, integration, e2e, etc.)
        2. Test frameworks: What testing libraries and runners are used
        3. Coverage: Estimated coverage level and any coverage configuration
        4. Quality tools: Linters, formatters, static analysis
        5. CI quality gates: What checks run before merge
        6. Test patterns: Common patterns used in tests (arrange/act/assert, fixtures, mocks)
        7. Areas with weak coverage (inferred from file structure)
        8. Code quality signals: Code style consistency, documentation level

        If specific features are not found, say so.
        Format as markdown.
        """;

        var reply = await CallLlmAsync(apiKey, model, prompt, ct);
        if (string.IsNullOrEmpty(reply)) return;

        var existing = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == Subtype)
            .ToListAsync(ct);
        Context.Enrichments.RemoveRange(existing);

        Context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Type = Type,
            Subtype = Subtype,
            Title = $"Quality & Testing for {repo.Name} ({testFiles.Count} test files)",
            Content = reply,
            Quality = EstimateQuality(reply),
            CreatedAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync(ct);
        Logger.LogInformation("Generated Quality docs for {Name}: {TestFiles} test files of {TotalFiles} total",
            repo.Name, testFiles.Count, totalFiles);
    }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        throw new NotSupportedException("This handler overrides HandleAsync directly.");
}
