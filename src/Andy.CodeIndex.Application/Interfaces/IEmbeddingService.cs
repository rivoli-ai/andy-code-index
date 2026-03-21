using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IEmbeddingService
{
    Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
    Task StoreEmbeddingsAsync(Guid[] enrichmentIds, float[][] embeddings, IndexType indexType, CancellationToken ct = default);
    int Dimensions { get; }
    string ModelName { get; }
    bool IsAvailable { get; }
}
