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

public class CreateOperationsDocsHandler : BaseLlmEnrichmentHandler
{
    private readonly IGitService _gitService;
    private readonly IndexingOptions _indexingOptions;

    public override TaskOperation Operation => TaskOperation.CreateOperationsDocs;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Operations;
    protected override EnrichmentType Type => EnrichmentType.Usage;

    public CreateOperationsDocsHandler(
        CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http,
        IGitService gitService, IOptions<IndexingOptions> indexingOptions,
        ILogger<CreateOperationsDocsHandler> logger)
        : base(context, resolver, opts, http, logger)
    {
        _gitService = gitService;
        _indexingOptions = indexingOptions.Value;
    }

    public override async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var trackedTask = await Context.IndexingTasks.FindAsync([task.Id], ct);
        if (trackedTask is not null)
        {
            trackedTask.ProgressMessage = "Analyzing operations...";
            trackedTask.Progress = 0;
            await Context.SaveChangesAsync(ct);
        }

        var repo = await Context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var (apiKey, baseUrl, model, source) = await ApiKeyResolver.ResolveLlmKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogInformation("Skipping {Operation} for {Name}: no LLM key available", Operation, repo.Name);
            return;
        }

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";

        // Detect operations-related files
        var opsFiles = new StringBuilder();
        var opsFilePatterns = new[] { "Dockerfile", "docker-compose*", ".github/workflows/*", "Jenkinsfile", "azure-pipelines*", ".gitlab-ci*", "Makefile", "Procfile", "*.tf", "*.helm*", "fly.toml", "railway.json", "nixpacks.toml" };

        foreach (var pattern in opsFilePatterns)
        {
            try
            {
                var files = await _gitService.ListFilesAsync(cloneDir, commitSha, pattern, ct);
                foreach (var file in files.Take(3))
                {
                    var content = await _gitService.ReadFileAsync(cloneDir, commitSha, file.Path, ct);
                    if (content is not null)
                    {
                        var truncated = content.Length > 1500 ? content[..1500] + "..." : content;
                        opsFiles.AppendLine($"--- {file.Path} ---");
                        opsFiles.AppendLine(truncated);
                        opsFiles.AppendLine();
                    }
                }
            }
            catch { /* pattern may not match */ }
        }

        var chunks = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .OrderBy(e => e.FilePath)
            .Take(15)
            .ToListAsync(ct);

        var prompt = $"""
        Analyze the operations and deployment setup of "{repo.Name}".

        Operations-related files found:
        {(opsFiles.Length > 0 ? opsFiles.ToString() : "No CI/CD or deployment files detected.")}

        Code context:
        {SummarizeChunks(chunks, 4000)}

        Document:
        1. Build and CI/CD: What pipelines exist, what they do, how builds are triggered
        2. Containerization: Docker setup, base images, multi-stage builds
        3. Deployment: Where and how the application is deployed
        4. Infrastructure: Any IaC (Terraform, Helm, CloudFormation)
        5. Monitoring: Health checks, logging, metrics, tracing setup
        6. Environment management: How different environments are configured
        7. Database migrations: How schema changes are applied
        8. Background jobs and scheduled tasks

        If specific features are not found, say so.
        Format as markdown.
        """;

        // Look up the commit record to set CommitId on enrichments
        Guid? commitId = null;
        if (commitSha != "HEAD")
        {
            var commitRecord = await Context.Commits
                .FirstOrDefaultAsync(c => c.RepositoryId == repo.Id && c.Sha == commitSha, ct);
            commitId = commitRecord?.Id;
        }

        var reply = await CallLlmAsync(apiKey, model, prompt, ct, baseUrl);
        if (string.IsNullOrEmpty(reply)) return;

        var existing = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == Subtype)
            .ToListAsync(ct);
        Context.Enrichments.RemoveRange(existing);

        Context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            CommitId = commitId,
            Type = Type,
            Subtype = Subtype,
            Title = $"Operations & Deployment for {repo.Name}",
            Content = reply,
            Quality = EstimateQuality(reply),
            CreatedAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync(ct);
        Logger.LogInformation("Generated Operations docs for {Name} ({Length} chars)", repo.Name, reply.Length);
    }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        throw new NotSupportedException("This handler overrides HandleAsync directly.");
}
