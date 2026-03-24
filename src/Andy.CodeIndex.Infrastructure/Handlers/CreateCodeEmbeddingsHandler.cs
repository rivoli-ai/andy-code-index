using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CreateCodeEmbeddingsHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IEmbeddingService _embeddingService;
    private readonly IApiKeyResolver _apiKeyResolver;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ILogger<CreateCodeEmbeddingsHandler> _logger;

    public TaskOperation Operation => TaskOperation.CreateCodeEmbeddings;

    public CreateCodeEmbeddingsHandler(
        CodeIndexDbContext context,
        IEmbeddingService embeddingService,
        IApiKeyResolver apiKeyResolver,
        IOptions<EmbeddingOptions> embeddingOptions,
        ILogger<CreateCodeEmbeddingsHandler> logger)
    {
        _context = context;
        _embeddingService = embeddingService;
        _apiKeyResolver = apiKeyResolver;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        // Check if any embedding key is available (user "anonymous" in dev, or system config)
        var (apiKey, source) = await _apiKeyResolver.ResolveEmbeddingKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogInformation("Skipping code embeddings for {Name}: no embedding API key available (set one in Settings or Embedding:ApiKey)", repo.Name);
            return;
        }

        _logger.LogInformation("Using embedding key from {Source} for {Name}", source, repo.Name);

        var chunks = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id &&
                        e.Subtype == EnrichmentSubtype.Chunk &&
                        !_context.ContentEmbeddings.Any(ce => ce.EnrichmentId == e.Id && ce.IndexType == IndexType.Code))
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            _logger.LogInformation("No new chunks to embed for {Name}", repo.Name);
            return;
        }

        _logger.LogInformation("Generating code embeddings for {Count} chunks in {Name}", chunks.Count, repo.Name);

        var texts = chunks.Select(c => c.Content).ToArray();
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, ct);
        var ids = chunks.Select(c => c.Id).ToArray();
        await _embeddingService.StoreEmbeddingsAsync(ids, embeddings, IndexType.Code, ct);

        _logger.LogInformation("Stored {Count} code embeddings for {Name}", embeddings.Length, repo.Name);
    }
}
