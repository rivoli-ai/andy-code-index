namespace Andy.CodeIndex.Application.Interfaces;

public interface IApiKeyResolver
{
    Task<(string? apiKey, string baseUrl, string model, string source)> ResolveEmbeddingKeyAsync(string? userId = null, CancellationToken ct = default);
    Task<(string? apiKey, string baseUrl, string model, string source)> ResolveLlmKeyAsync(string? userId = null, CancellationToken ct = default);
}
