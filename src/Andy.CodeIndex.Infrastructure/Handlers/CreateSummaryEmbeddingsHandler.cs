using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CreateSummaryEmbeddingsHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IEmbeddingService _embeddingService;
    private readonly IApiKeyResolver _apiKeyResolver;
    private readonly ILogger<CreateSummaryEmbeddingsHandler> _logger;

    public TaskOperation Operation => TaskOperation.CreateSummaryEmbeddings;

    public CreateSummaryEmbeddingsHandler(
        CodeIndexDbContext context, IEmbeddingService embeddingService,
        IApiKeyResolver apiKeyResolver, ILogger<CreateSummaryEmbeddingsHandler> logger)
    {
        _context = context;
        _embeddingService = embeddingService;
        _apiKeyResolver = apiKeyResolver;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var trackedTask = await _context.IndexingTasks.FindAsync([task.Id], ct);
        if (trackedTask is not null)
        {
            trackedTask.ProgressMessage = "Generating summary embeddings...";
            trackedTask.Progress = 0;
            await _context.SaveChangesAsync(ct);
        }

        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var (apiKey, baseUrl, model, source) = await _apiKeyResolver.ResolveEmbeddingKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogInformation("Skipping summary embeddings for {Name}: no embedding key", repo.Name);
            return;
        }

        // Embed text enrichments (summaries, not code chunks — those are IndexType.Code)
        var summaries = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id &&
                        e.Type != EnrichmentType.Development && // Exclude code chunks
                        !_context.ContentEmbeddings.Any(ce => ce.EnrichmentId == e.Id && ce.IndexType == IndexType.Text))
            .ToListAsync(ct);

        if (summaries.Count == 0)
        {
            _logger.LogInformation("No new summaries to embed for {Name}", repo.Name);
            return;
        }

        _logger.LogInformation("Generating text embeddings for {Count} enrichments in {Name}", summaries.Count, repo.Name);

        var texts = summaries.Select(s => s.Content.Length > 8000 ? s.Content[..8000] : s.Content).ToArray();
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, ct);
        var ids = summaries.Select(s => s.Id).ToArray();
        await _embeddingService.StoreEmbeddingsAsync(ids, embeddings, IndexType.Text, ct);

        _logger.LogInformation("Stored {Count} text embeddings for {Name}", embeddings.Length, repo.Name);
    }
}
