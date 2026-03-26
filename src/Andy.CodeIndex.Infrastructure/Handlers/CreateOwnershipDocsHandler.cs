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

public class CreateOwnershipDocsHandler : BaseLlmEnrichmentHandler
{
    private readonly IGitService _gitService;
    private readonly IndexingOptions _indexingOptions;

    public override TaskOperation Operation => TaskOperation.CreateOwnershipDocs;
    protected override EnrichmentSubtype Subtype => EnrichmentSubtype.Ownership;
    protected override EnrichmentType Type => EnrichmentType.Architecture;

    public CreateOwnershipDocsHandler(
        CodeIndexDbContext context, IApiKeyResolver resolver,
        IOptions<EnrichmentLlmOptions> opts, IHttpClientFactory http,
        IGitService gitService, IOptions<IndexingOptions> indexingOptions,
        ILogger<CreateOwnershipDocsHandler> logger)
        : base(context, resolver, opts, http, logger)
    {
        _gitService = gitService;
        _indexingOptions = indexingOptions.Value;
    }

    protected override string BuildPrompt(Repository repo, List<Enrichment> chunks)
    {
        // Try to find CODEOWNERS content from chunks
        var codeownersChunk = chunks.FirstOrDefault(c =>
            c.FilePath != null && c.FilePath.Contains("CODEOWNERS", StringComparison.OrdinalIgnoreCase));
        var codeownersContent = codeownersChunk?.Content ?? "No CODEOWNERS file found.";

        return $"""
        Analyze the repository "{repo.Name}" and document its ownership and collaboration structure.

        CODEOWNERS file:
        {codeownersContent}

        Based on the code structure below, identify:
        1. Primary maintainers and their areas of responsibility
        2. Team or organizational ownership patterns
        3. Areas with clear vs ambiguous ownership
        4. Contribution workflow (if visible from code structure)
        5. Key reviewers and subject matter experts (inferred from code organization)

        Format as markdown with clear sections.

        {SummarizeChunks(chunks)}
        """;
    }
}
