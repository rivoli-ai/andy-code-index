namespace Andy.CodeIndex.Application.Interfaces;

public interface IEmbeddingProvider
{
    Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
    int Dimensions { get; }
    string ModelName { get; }
    bool IsAvailable { get; }
}
