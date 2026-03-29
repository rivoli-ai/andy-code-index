using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CreateSecurityDocsHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateSecurityDocs;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Security;
    protected override EnrichmentType Type => EnrichmentType.Architecture;

    public CreateSecurityDocsHandler(
        CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http,
        ILogger<CreateSecurityDocsHandler> logger)
        : base(context, resolver, opts, http, logger) { }

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

        // Get security-related chunks by filtering file paths
        var securityChunks = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .Where(e => e.FilePath != null && (
                e.FilePath.Contains("Auth") || e.FilePath.Contains("auth") ||
                e.FilePath.Contains("Security") || e.FilePath.Contains("security") ||
                e.FilePath.Contains("Middleware") || e.FilePath.Contains("middleware") ||
                e.FilePath.Contains("Guard") || e.FilePath.Contains("guard") ||
                e.FilePath.Contains("Permission") || e.FilePath.Contains("permission") ||
                e.FilePath.Contains("Encrypt") || e.FilePath.Contains("encrypt") ||
                e.FilePath.Contains("Token") || e.FilePath.Contains("token") ||
                e.FilePath.Contains(".env") || e.FilePath.Contains("secret")))
            .OrderBy(e => e.FilePath)
            .Take(30)
            .ToListAsync(ct);

        // Fall back to general chunks if no security-specific ones found
        if (securityChunks.Count < 5)
        {
            var generalChunks = await Context.Enrichments
                .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Chunk)
                .OrderBy(e => e.FilePath)
                .Take(30)
                .ToListAsync(ct);
            securityChunks = generalChunks;
        }

        // Look up the commit record to set CommitId on enrichments
        var commitSha = repo.LastIndexedCommitSha;
        Guid? commitId = null;
        if (commitSha != null)
        {
            var commitRecord = await Context.Commits
                .FirstOrDefaultAsync(c => c.RepositoryId == repo.Id && c.Sha == commitSha, ct);
            commitId = commitRecord?.Id;
        }

        var prompt = BuildPrompt(repo, securityChunks);
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
            CommitId = commitId,
            Type = Type,
            Subtype = Subtype,
            Title = $"Security Analysis for {repo.Name}",
            Content = reply,
            Quality = EstimateQuality(reply),
            CreatedAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync(ct);
        Logger.LogInformation("Generated Security docs for {Name} ({Length} chars)", repo.Name, reply.Length);
    }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Analyze the security architecture of "{repo.Name}".

        Document:
        1. Authentication: How users/services authenticate (JWT, OAuth, API keys, etc.)
        2. Authorization: How permissions and access control work (RBAC, policies, guards)
        3. Secrets management: How API keys, credentials, and secrets are stored and accessed
        4. Input validation: What validation and sanitization patterns are used
        5. Encryption: What encryption is used for data at rest and in transit
        6. Security headers and CORS configuration
        7. Sensitive file paths (.env, credentials, certificates)
        8. Known security patterns and potential concerns

        If specific security features are not found, say so clearly.
        Format as markdown.

        {SummarizeChunks(chunks)}
        """;
}
