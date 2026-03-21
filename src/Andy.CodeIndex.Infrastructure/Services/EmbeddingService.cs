using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Andy.CodeIndex.Infrastructure.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;
    private readonly CodeIndexDbContext _context;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<EmbeddingService> _logger;

    public int Dimensions => _provider.Dimensions;
    public string ModelName => _provider.ModelName;
    public bool IsAvailable => _provider.IsAvailable;

    public EmbeddingService(
        IEmbeddingProvider provider,
        CodeIndexDbContext context,
        IOptions<EmbeddingOptions> options,
        ILogger<EmbeddingService> logger)
    {
        _provider = provider;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        if (texts.Length == 0)
            return [];

        // Split into batches respecting size limits
        var batches = CreateBatches(texts);
        var allEmbeddings = new List<float[]>();

        // Process batches with configured parallelism
        var semaphore = new SemaphoreSlim(_options.Parallelism);

        var tasks = batches.Select(async batch =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await _provider.GenerateEmbeddingsAsync(batch, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            allEmbeddings.AddRange(result);

        return allEmbeddings.ToArray();
    }

    public async Task StoreEmbeddingsAsync(Guid[] enrichmentIds, float[][] embeddings, IndexType indexType, CancellationToken ct = default)
    {
        if (enrichmentIds.Length != embeddings.Length)
            throw new ArgumentException("enrichmentIds and embeddings must have the same length");

        var entities = new List<ContentEmbedding>();
        for (var i = 0; i < enrichmentIds.Length; i++)
        {
            entities.Add(new ContentEmbedding
            {
                Id = Guid.NewGuid(),
                EnrichmentId = enrichmentIds[i],
                EmbeddingVector = new Vector(embeddings[i]),
                IndexType = indexType,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.ContentEmbeddings.AddRangeAsync(entities, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Stored {Count} {Type} embeddings", entities.Count, indexType);
    }

    internal List<string[]> CreateBatches(string[] texts)
    {
        var batches = new List<string[]>();
        var currentBatch = new List<string>();
        var currentChars = 0;

        foreach (var text in texts)
        {
            if (currentBatch.Count >= _options.MaxBatchSize ||
                (currentChars + text.Length > _options.MaxBatchChars && currentBatch.Count > 0))
            {
                batches.Add(currentBatch.ToArray());
                currentBatch.Clear();
                currentChars = 0;
            }

            currentBatch.Add(text);
            currentChars += text.Length;
        }

        if (currentBatch.Count > 0)
            batches.Add(currentBatch.ToArray());

        return batches;
    }
}
