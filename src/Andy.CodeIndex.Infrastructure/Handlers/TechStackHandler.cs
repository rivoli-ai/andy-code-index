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

public class TechStackHandler : BaseLlmEnrichmentHandler
{
    private readonly IGitService _gitService;
    private readonly IndexingOptions _indexingOptions;

    public override TaskOperation Operation => TaskOperation.CreateTechStack;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.TechStack;
    protected override EnrichmentType Type => EnrichmentType.Insights;

    public TechStackHandler(
        CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http,
        IGitService gitService, IOptions<IndexingOptions> indexingOptions,
        ILogger<TechStackHandler> logger)
        : base(context, resolver, opts, http, logger)
    {
        _gitService = gitService;
        _indexingOptions = indexingOptions.Value;
    }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        throw new NotSupportedException("This handler overrides HandleAsync directly.");

    public override async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await Context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var (apiKey, baseUrl, model, source) = await ApiKeyResolver.ResolveLlmKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogInformation("Skipping {Operation} for {Name}: no LLM key available", Operation, repo.Name);
            return;
        }

        // Resolve commit ID
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        Guid? commitId = null;
        if (repo.LastIndexedCommitSha != null)
        {
            var commitRecord = await Context.Commits
                .FirstOrDefaultAsync(c => c.RepositoryId == repo.Id && c.Sha == repo.LastIndexedCommitSha, ct);
            commitId = commitRecord?.Id;
        }

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repo.Id);

        // Gather file list for language detection
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);
        var languageBreakdown = BuildLanguageBreakdown(files);

        // Read existing Dependencies enrichment for package versions
        var depsEnrichment = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Dependencies)
            .FirstOrDefaultAsync(ct);
        var depsContext = depsEnrichment?.Content ?? "";
        if (depsContext.Length > 3000) depsContext = depsContext[..3000] + "\n... (truncated)";

        // Read key config files
        var configContext = await ReadConfigFilesAsync(cloneDir, commitSha, files, ct);

        // Build the prompt
        var prompt = $"""
            Analyze the technology stack of the repository "{repo.Name}" based on the information below.
            Output ONLY markdown. No preamble. Be specific with version numbers.

            Produce a structured summary with the following sections:

            ## Backend
            Detected backend framework(s) and version(s). Include the runtime (e.g., .NET 8, Node.js 20, Go 1.21).

            ## Frontend
            Detected frontend framework(s) and version(s) (e.g., Angular 17, React 18).

            ## Database
            Detected database technologies from docker-compose, connection strings, ORM configs.

            ## Infrastructure
            Docker, Kubernetes, CI/CD tools detected from config files.

            ## Languages
            Breakdown with file counts (use the data provided).

            ## Key Dependencies
            Major packages with versions from the dependency data.

            === Language Breakdown ===
            {languageBreakdown}

            === Dependencies ===
            {depsContext}

            === Config Files ===
            {configContext}
            """;

        var reply = await CallLlmAsync(apiKey, model, prompt, ct, baseUrl);
        if (string.IsNullOrEmpty(reply)) return;

        // Delete existing TechStack enrichments
        var existing = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.TechStack)
            .ToListAsync(ct);
        Context.Enrichments.RemoveRange(existing);

        Context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            CommitId = commitId,
            Type = EnrichmentType.Insights,
            Subtype = EnrichmentSubtype.TechStack,
            Title = $"Tech Stack for {repo.Name}",
            Content = reply,
            Quality = EstimateQuality(reply),
            CreatedAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync(ct);
        Logger.LogInformation("Generated TechStack for {Name} ({Length} chars)", repo.Name, reply.Length);
    }

    internal static string BuildLanguageBreakdown(List<GitFileInfo> files)
    {
        var sb = new StringBuilder();
        var total = files.Count;
        if (total == 0) return "No files found.";

        var byLang = files
            .Where(f => !string.IsNullOrEmpty(f.Language))
            .GroupBy(f => f.Language!)
            .Select(g => new { Language = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        sb.AppendLine("| Language | Files | Percentage |");
        sb.AppendLine("|----------|-------|------------|");
        foreach (var lang in byLang)
        {
            var pct = Math.Round(100.0 * lang.Count / total, 1);
            sb.AppendLine($"| {lang.Language} | {lang.Count} | {pct}% |");
        }

        // Count files without a detected language
        var noLang = files.Count(f => string.IsNullOrEmpty(f.Language));
        if (noLang > 0)
        {
            var pct = Math.Round(100.0 * noLang / total, 1);
            sb.AppendLine($"| Other | {noLang} | {pct}% |");
        }

        sb.AppendLine($"\nTotal files: {total}");
        return sb.ToString();
    }

    private async Task<string> ReadConfigFilesAsync(
        string cloneDir, string commitSha, List<GitFileInfo> files, CancellationToken ct)
    {
        // Key config file patterns to look for
        var configPatterns = new[]
        {
            "*.csproj", "package.json", "go.mod", "Cargo.toml",
            "docker-compose.yml", "docker-compose.yaml",
            "Dockerfile", "angular.json", ".github/workflows/*.yml",
            ".github/workflows/*.yaml", "Jenkinsfile", ".gitlab-ci.yml",
            "requirements.txt", "pyproject.toml", "pom.xml", "build.gradle"
        };

        var configFiles = files
            .Where(f => MatchesAnyPattern(f.Path, configPatterns))
            .Take(15) // Limit to avoid too much context
            .ToList();

        var sb = new StringBuilder();
        foreach (var file in configFiles)
        {
            var content = await _gitService.ReadFileAsync(cloneDir, commitSha, file.Path, ct);
            if (content is null) continue;

            // Truncate large files
            if (content.Length > 2000) content = content[..2000] + "\n... (truncated)";

            sb.AppendLine($"--- {file.Path} ---");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    internal static bool MatchesAnyPattern(string filePath, string[] patterns)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var pattern in patterns)
        {
            if (pattern.Contains('/'))
            {
                // Directory-aware pattern
                if (MatchGlobPath(filePath, pattern)) return true;
            }
            else if (pattern.StartsWith("*."))
            {
                // Extension pattern
                var ext = pattern[1..]; // e.g., ".csproj"
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else
            {
                // Exact filename match
                if (fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static bool MatchGlobPath(string filePath, string pattern)
    {
        // Simple glob: .github/workflows/*.yml
        var parts = pattern.Split('/');
        var pathParts = filePath.Replace('\\', '/').Split('/');

        if (pathParts.Length < parts.Length) return false;

        // Try to match from the end
        for (int offset = 0; offset <= pathParts.Length - parts.Length; offset++)
        {
            bool match = true;
            for (int i = 0; i < parts.Length; i++)
            {
                var pat = parts[i];
                var seg = pathParts[offset + i];

                if (pat.StartsWith("*."))
                {
                    var ext = pat[1..];
                    if (!seg.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { match = false; break; }
                }
                else if (!seg.Equals(pat, StringComparison.OrdinalIgnoreCase))
                {
                    match = false; break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}
