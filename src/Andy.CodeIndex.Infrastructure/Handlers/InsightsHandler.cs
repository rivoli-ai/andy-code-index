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

public class InsightsHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateInsights;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.FeatureMap; // default, overridden per layer
    protected override EnrichmentType Type => EnrichmentType.Insights;

    public InsightsHandler(
        CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http,
        ILogger<InsightsHandler> logger)
        : base(context, resolver, opts, http, logger) { }

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
        var commitSha = repo.LastIndexedCommitSha;
        Guid? commitId = null;
        if (commitSha != null)
        {
            var commitRecord = await Context.Commits
                .FirstOrDefaultAsync(c => c.RepositoryId == repo.Id && c.Sha == commitSha, ct);
            commitId = commitRecord?.Id;
        }

        // Gather existing enrichments as context (build on what exists, not from scratch)
        var existingContext = await BuildExistingContext(repo.Id, ct);

        // Get sample code chunks
        var chunks = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .OrderBy(e => e.FilePath)
            .Take(30)
            .ToListAsync(ct);
        var codeContext = SummarizeChunks(chunks);

        // Generate all 10 insight layers sequentially
        var layers = GetInsightLayers(repo, existingContext, codeContext);
        var generatedCount = 0;

        const string systemInstruction = """
            IMPORTANT RULES:
            - Output ONLY the requested content in well-formatted markdown.
            - Do NOT include any preamble, explanation, or meta-commentary about what you are doing.
            - Do NOT say "I'll analyze..." or "Based on the provided context..." or similar.
            - Do NOT output raw JSON unless the format specifically requests it — prefer markdown tables.
            - Start directly with the content (headings, lists, diagrams).
            - Use the provided context to give specific, accurate analysis — not generic templates.
            """;

        foreach (var layer in layers)
        {
            // Update progress before generating
            task.Progress = (int)((generatedCount / (float)layers.Count) * 100);
            task.ProgressMessage = $"Generating {layer.Title} ({generatedCount}/{layers.Count})";
            await Context.SaveChangesAsync(ct);

            var prompt = systemInstruction + "\n\n" + layer.Prompt;
            var reply = await CallLlmAsync(apiKey, model, prompt, ct, baseUrl);
            if (string.IsNullOrEmpty(reply)) continue;

            // Delete existing enrichment of this subtype for the repo
            var existing = await Context.Enrichments
                .Where(e => e.RepositoryId == repo.Id && e.Subtype == layer.Subtype)
                .ToListAsync(ct);
            Context.Enrichments.RemoveRange(existing);

            Context.Enrichments.Add(new Enrichment
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                CommitId = commitId,
                Type = EnrichmentType.Insights,
                Subtype = layer.Subtype,
                Title = $"{layer.Title} for {repo.Name}",
                Content = reply,
                Quality = EstimateQuality(reply),
                CreatedAt = DateTime.UtcNow
            });

            generatedCount++;

            // Update progress after generating
            task.Progress = (int)((generatedCount / (float)layers.Count) * 100);
            task.ProgressMessage = $"Generated {layer.Title} ({generatedCount}/{layers.Count})";
            await Context.SaveChangesAsync(ct);

            Logger.LogInformation("Generated insight layer {Layer} for {Name} ({Length} chars)",
                layer.Subtype, repo.Name, reply.Length);
        }

        // Invalidate cached report since insights have changed
        var staleReports = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.InsightReport)
            .ToListAsync(ct);
        if (staleReports.Count > 0)
        {
            Context.Enrichments.RemoveRange(staleReports);
            await Context.SaveChangesAsync(ct);
            Logger.LogInformation("Invalidated {Count} cached report(s) for {Name}", staleReports.Count, repo.Name);
        }

        Logger.LogInformation("Completed {Count}/11 insight layers for {Name}", generatedCount, repo.Name);
    }

    private async Task<string> BuildExistingContext(Guid repoId, CancellationToken ct)
    {
        var contextSubtypes = new[]
        {
            EnrichmentSubtype.Physical,
            EnrichmentSubtype.Dependencies,
            EnrichmentSubtype.Wiki,
            EnrichmentSubtype.Quality,
            EnrichmentSubtype.Security,
            EnrichmentSubtype.Operations,
            EnrichmentSubtype.Ownership,
            EnrichmentSubtype.CommitHistory,
            EnrichmentSubtype.TechStack
        };

        var enrichments = await Context.Enrichments
            .Where(e => e.RepositoryId == repoId && contextSubtypes.Contains(e.Subtype))
            .ToListAsync(ct);

        var sb = new StringBuilder();
        foreach (var enrichment in enrichments)
        {
            var content = enrichment.Content.Length > 3000
                ? enrichment.Content[..3000] + "..."
                : enrichment.Content;
            sb.AppendLine($"=== {enrichment.Subtype} ===");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    internal static List<InsightLayer> GetInsightLayers(Repository repo, string existingContext, string codeContext)
    {
        var repoName = repo.Name;
        return
        [
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.FeatureMap,
                Title = "Feature Map",
                Prompt = $"""
                    Analyze the repository "{repoName}" and create a comprehensive feature inventory.
                    List ALL features and capabilities you can identify — aim for at least 10-20 features.
                    Look at controllers, services, API endpoints, UI components, CLI commands, background jobs, integrations.
                    For each feature, assign a stable ID in format feat:[category]:[name] (e.g., feat:auth:login, feat:search:semantic).

                    Present as a markdown table with columns: ID, Feature Name, Description, Entry Files, Status (active/deprecated), Complexity (low/medium/high).
                    Group features by category (e.g., ## Authentication, ## Search, ## Data Management) with section headings.
                    Be thorough — a real application typically has many features across different areas.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.ArchitectureAnalysis,
                Title = "Architecture Analysis",
                Prompt = $"""
                    Create a detailed technical architecture analysis of "{repoName}".
                    Include:
                    1. Architecture overview (layers, components, their responsibilities)
                    2. Communication patterns (HTTP, gRPC, message queues, etc.)
                    3. Data flow (how data moves through the system)
                    4. External integrations

                    You MUST include at least one Mermaid diagram. Use this format:
                    ```mermaid
                    graph TD
                        A[Component A] --> B[Component B]
                    ```
                    Prefer graph TD, flowchart, or C4 component diagrams. Make them detailed with real component names from the codebase.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.DesignAnalysis,
                Title = "Design Analysis",
                Prompt = $"""
                    Analyze the technical design of "{repoName}" in detail.
                    Include:
                    1. Domain model — entities and their relationships
                    2. API surface — endpoints, methods, authentication
                    3. Design patterns used (MVC, Repository, CQRS, etc.)
                    4. Error handling approach
                    5. State management

                    You MUST include a Mermaid class diagram or ER diagram showing the domain model:
                    ```mermaid
                    classDiagram
                        class Entity1
                        Entity1 --> Entity2
                    ```
                    Use real entity names from the codebase. Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.ImplementationAnalysis,
                Title = "Implementation Analysis",
                Prompt = $"""
                    Analyze the implementation quality of "{repoName}".
                    Identify: key code patterns, code smells, cross-language consistency, top 5 improvement suggestions with effort/impact.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.DependencyAnalysis,
                Title = "Dependency Analysis",
                Prompt = $"""
                    Analyze all dependencies of "{repoName}".
                    Include: dependency count, categories (runtime/dev/test), potentially outdated packages, license types, security advisories if detectable.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.TestAnalysis,
                Title = "Test Analysis",
                Prompt = $"""
                    Analyze the testing strategy of "{repoName}".
                    Include: test pyramid shape (unit/integration/e2e counts), test frameworks, coverage estimate, testing patterns, gaps, top 3 testing improvements.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.SecurityAnalysis,
                Title = "Security Analysis",
                Prompt = $"""
                    Perform a security analysis of "{repoName}".
                    Check: authentication patterns, secrets handling, input validation, OWASP Top 10 exposure, security headers, rate limiting.
                    Rate each area risk 1-5.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.DeploymentAnalysis,
                Title = "Deployment Analysis",
                Prompt = $"""
                    Analyze the deployment and CI/CD setup of "{repoName}".
                    Include: pipeline description (Mermaid flowchart), environments, release process, containerization, infrastructure-as-code.
                    Include a ```mermaid block with a flowchart.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.OperationsAnalysis,
                Title = "Operations Analysis",
                Prompt = $"""
                    Audit the operational readiness of "{repoName}".
                    Check: logging patterns (correct levels, no PII), monitoring, health checks, alerting, error handling, graceful shutdown.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.LocalSetupGuide,
                Title = "Local Setup Guide",
                Prompt = $"""
                    Generate a getting-started guide for "{repoName}".
                    Include: prerequisites, step-by-step setup, running tests, common issues, environment variables needed.
                    Format as markdown.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            },
            new InsightLayer
            {
                Subtype = EnrichmentSubtype.TechStack,
                Title = "Technology Stack",
                Prompt = $"""
                    Summarize the technology stack of "{repoName}" in a concise markdown format.
                    Include: Backend frameworks + versions, Frontend frameworks + versions,
                    Database technologies, Infrastructure (Docker, K8s, CI/CD), Languages breakdown,
                    and Key Dependencies with versions.
                    Output ONLY markdown. No preamble. Be specific with version numbers.

                    Existing knowledge:
                    {existingContext}

                    Code samples:
                    {codeContext}
                    """
            }
        ];
    }

    internal class InsightLayer
    {
        public required EnrichmentSubtype Subtype { get; init; }
        public required string Title { get; init; }
        public required string Prompt { get; init; }
    }
}
