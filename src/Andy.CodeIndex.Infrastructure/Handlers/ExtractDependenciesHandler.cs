using System.Text;
using System.Text.Json;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class ExtractDependenciesHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IDependencyParserService _parser;
    private readonly IndexingOptions _options;
    private readonly ILogger<ExtractDependenciesHandler> _logger;

    public TaskOperation Operation => TaskOperation.ExtractDependencies;

    public ExtractDependenciesHandler(
        CodeIndexDbContext context, IGitService gitService,
        IDependencyParserService parser,
        IOptions<IndexingOptions> options, ILogger<ExtractDependenciesHandler> logger)
    {
        _context = context;
        _gitService = gitService;
        _parser = parser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);

        var allDeps = new List<PackageDependency>();

        foreach (var file in files.Where(f => _parser.CanParse(f.Path)))
        {
            var content = await _gitService.ReadFileAsync(cloneDir, commitSha, file.Path, ct);
            if (content is null) continue;

            var deps = _parser.Parse(file.Path, content);
            allDeps.AddRange(deps);
        }

        // Delete existing dependency enrichments
        var existing = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Dependencies)
            .ToListAsync(ct);
        _context.Enrichments.RemoveRange(existing);

        if (allDeps.Count > 0)
        {
            // Build markdown content
            var md = new StringBuilder();
            md.AppendLine($"# Dependencies for {repo.Name}");
            md.AppendLine();
            md.AppendLine($"Total: {allDeps.Count} packages from {allDeps.Select(d => d.SourceFile).Distinct().Count()} files");
            md.AppendLine();

            foreach (var group in allDeps.GroupBy(d => d.Source).OrderBy(g => g.Key))
            {
                md.AppendLine($"## {group.Key} ({group.Count()} packages)");
                md.AppendLine();
                md.AppendLine("| Package | Version | Scope | Source File |");
                md.AppendLine("|---------|---------|-------|-------------|");
                foreach (var dep in group.OrderBy(d => d.Name))
                {
                    md.AppendLine($"| {dep.Name} | {dep.Version ?? "-"} | {dep.Scope} | {dep.SourceFile} |");
                }
                md.AppendLine();
            }

            _context.Enrichments.Add(new Enrichment
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Type = EnrichmentType.Architecture,
                Subtype = EnrichmentSubtype.Dependencies,
                Title = $"Dependencies ({allDeps.Count} packages)",
                Content = md.ToString(),
                Quality = allDeps.Count > 10 ? 1.0 : allDeps.Count > 3 ? 0.8 : 0.5,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Extracted {Count} dependencies from {Files} files for {Name}",
            allDeps.Count, files.Count(f => _parser.CanParse(f.Path)), repo.Name);
    }
}
