using System.Net.Http.Headers;
using System.Net.Http.Json;
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

/// <summary>Base class for LLM-powered enrichment handlers.</summary>
public abstract class BaseLlmEnrichmentHandler : ITaskHandler
{
    protected readonly CodeIndexDbContext Context;
    protected readonly IApiKeyResolver ApiKeyResolver;
    protected readonly EnrichmentLlmOptions LlmOptions;
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;

    public abstract TaskOperation Operation { get; }
    protected abstract EnrichmentSubtype Subtype { get; }
    protected abstract EnrichmentType Type { get; }
    protected abstract string BuildPrompt(Repository repo, List<Enrichment> existingChunks);

    protected BaseLlmEnrichmentHandler(
        CodeIndexDbContext context, IApiKeyResolver apiKeyResolver,
        IOptions<EnrichmentLlmOptions> llmOptions, IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        Context = context;
        ApiKeyResolver = apiKeyResolver;
        LlmOptions = llmOptions.Value;
        HttpClientFactory = httpClientFactory;
        Logger = logger;
    }

    public virtual async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await Context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var (apiKey, model, source) = await ApiKeyResolver.ResolveLlmKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogInformation("Skipping {Operation} for {Name}: no LLM key available", Operation, repo.Name);
            return;
        }

        // Get sample chunks for context
        var chunks = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.Chunk)
            .OrderBy(e => e.FilePath)
            .Take(30)
            .ToListAsync(ct);

        var prompt = BuildPrompt(repo, chunks);

        // Call LLM
        var reply = await CallLlmAsync(apiKey, model, prompt, ct);
        if (string.IsNullOrEmpty(reply)) return;

        // Delete existing enrichments of this subtype for the repo
        var existing = await Context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == Subtype)
            .ToListAsync(ct);
        Context.Enrichments.RemoveRange(existing);

        // Store new enrichment with quality score
        Context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Type = Type,
            Subtype = Subtype,
            Title = $"{Subtype} for {repo.Name}",
            Content = reply,
            Quality = EstimateQuality(reply),
            CreatedAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync(ct);
        Logger.LogInformation("Generated {Subtype} for {Name} ({Length} chars)", Subtype, repo.Name, reply.Length);
    }

    protected async Task<string?> CallLlmAsync(string apiKey, string model, string prompt, CancellationToken ct)
    {
        var client = HttpClientFactory.CreateClient("Chat");
        client.BaseAddress = new Uri(LlmOptions.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = TimeSpan.FromSeconds(LlmOptions.TimeoutSeconds);

        var request = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 3000,
            temperature = 0.3
        };

        try
        {
            var response = await client.PostAsJsonAsync("chat/completions", request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "LLM call failed for {Operation}", Operation);
            return null;
        }
    }

    internal static double EstimateQuality(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var lower = content.ToLowerInvariant();
        var length = content.Length;

        // Low-quality indicators: LLM said it couldn't find anything useful
        var lowQualityPhrases = new[]
        {
            "no database schema", "no schema found", "unable to determine", "cannot determine",
            "no information available", "not enough context", "no relevant", "i cannot",
            "i don't have enough", "no data available", "could not find", "unable to find",
            "no specific", "not found in the", "insufficient", "no evidence of"
        };

        var hasLowQualityPhrase = lowQualityPhrases.Any(p => lower.Contains(p));

        if (hasLowQualityPhrase && length < 500) return 0.1;
        if (hasLowQualityPhrase) return 0.3;
        if (length < 100) return 0.2;
        if (length < 300) return 0.5;
        if (length < 1000) return 0.7;
        return length >= 2000 ? 1.0 : 0.85;
    }

    protected string SummarizeChunks(List<Enrichment> chunks, int maxChars = 8000)
    {
        var summary = new System.Text.StringBuilder();
        foreach (var chunk in chunks)
        {
            if (summary.Length > maxChars) break;
            summary.AppendLine($"--- {chunk.FilePath} (lines {chunk.StartLine}-{chunk.EndLine}, {chunk.Language}) ---");
            var content = chunk.Content.Length > 300 ? chunk.Content[..300] + "..." : chunk.Content;
            summary.AppendLine(content);
            summary.AppendLine();
        }
        return summary.ToString();
    }
}

// --- Concrete handlers ---

public class CreateArchitectureDocsHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateArchitectureDocs;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Physical;
    protected override EnrichmentType Type => EnrichmentType.Architecture;

    public CreateArchitectureDocsHandler(CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http, ILogger<CreateArchitectureDocsHandler> logger)
        : base(context, resolver, opts, http, logger) { }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Analyze the following code from the repository "{repo.Name}" and provide a high-level architecture overview.
        Include: main components, their responsibilities, how they interact, data flow, and key design patterns.
        Format as markdown.

        {SummarizeChunks(chunks)}
        """;
}

public class CreateDatabaseSchemaHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateDatabaseSchema;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.DatabaseSchema;
    protected override EnrichmentType Type => EnrichmentType.Architecture;

    public CreateDatabaseSchemaHandler(CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http, ILogger<CreateDatabaseSchemaHandler> logger)
        : base(context, resolver, opts, http, logger) { }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Analyze the following code from "{repo.Name}" and document the database schema.
        Look for: entity classes, migrations, DbContext configurations, table definitions.
        Include: tables, columns, relationships, indexes, and constraints.
        If no database schema is found, say so. Format as markdown.

        {SummarizeChunks(chunks)}
        """;
}

public class CreateCommitDescriptionHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateCommitDescription;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.CommitDescription;
    protected override EnrichmentType Type => EnrichmentType.History;

    public CreateCommitDescriptionHandler(CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http, ILogger<CreateCommitDescriptionHandler> logger)
        : base(context, resolver, opts, http, logger) { }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Based on the code structure of "{repo.Name}", describe the development history and evolution.
        What are the main features? What technologies are used? What's the overall project purpose?
        Format as markdown.

        {SummarizeChunks(chunks)}
        """;
}

public class CreateCookbookHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateCookbook;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Cookbook;
    protected override EnrichmentType Type => EnrichmentType.Usage;

    public CreateCookbookHandler(CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http, ILogger<CreateCookbookHandler> logger)
        : base(context, resolver, opts, http, logger) { }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Create a cookbook/getting-started guide for the repository "{repo.Name}".
        Include: how to set up the project, common usage patterns, code examples,
        configuration, and best practices. Format as markdown with code blocks.

        {SummarizeChunks(chunks)}
        """;
}

public class CreateWikiHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateWiki;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Wiki;
    protected override EnrichmentType Type => EnrichmentType.Usage;

    public CreateWikiHandler(CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http, ILogger<CreateWikiHandler> logger)
        : base(context, resolver, opts, http, logger) { }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Create comprehensive wiki documentation for the repository "{repo.Name}".
        Include sections: Overview, Architecture, API Reference, Configuration,
        Deployment, Testing, and Troubleshooting.
        Format as markdown with a table of contents at the top.

        {SummarizeChunks(chunks)}
        """;
}

public class CreateSummaryEnrichmentsHandler : BaseLlmEnrichmentHandler
{
    public override TaskOperation Operation => TaskOperation.CreateSummaryEnrichments;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.SnippetSummary;
    protected override EnrichmentType Type => EnrichmentType.Development;

    public CreateSummaryEnrichmentsHandler(CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http, ILogger<CreateSummaryEnrichmentsHandler> logger)
        : base(context, resolver, opts, http, logger) { }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks) =>
        $"""
        Summarize the following code snippets from "{repo.Name}" into concise natural language descriptions.
        For each file mentioned, describe what it does in 1-2 sentences.
        Group by file path. Format as markdown.

        {SummarizeChunks(chunks)}
        """;
}
