using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;

    public int Dimensions => _options.GetDimensions();
    public string ModelName => _options.Model;
    public bool IsAvailable => _options.IsConfigured;

    public OpenAiEmbeddingProvider(
        HttpClient httpClient,
        IOptions<EmbeddingOptions> options,
        ILogger<OpenAiEmbeddingProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        if (!string.IsNullOrEmpty(_options.ApiKey))
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        if (texts.Length == 0)
            return [];

        var request = new EmbeddingRequest
        {
            Input = texts,
            Model = _options.Model
        };

        var retryCount = 0;
        var delay = TimeSpan.FromSeconds(2);

        while (true)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("embeddings", request, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (retryCount >= _options.MaxRetries)
                        throw new HttpRequestException($"Rate limited after {retryCount} retries");

                    var retryAfter = response.Headers.RetryAfter?.Delta ?? delay;
                    _logger.LogWarning("Rate limited, retrying after {Delay}s (attempt {Retry}/{Max})",
                        retryAfter.TotalSeconds, retryCount + 1, _options.MaxRetries);

                    await Task.Delay(retryAfter, ct);
                    retryCount++;
                    delay *= 2; // Exponential backoff
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Empty response from embedding API");

                return result.Data
                    .OrderBy(d => d.Index)
                    .Select(d => d.Embedding)
                    .ToArray();
            }
            catch (HttpRequestException ex) when (retryCount < _options.MaxRetries && !ct.IsCancellationRequested)
            {
                retryCount++;
                _logger.LogWarning(ex, "Embedding request failed, retrying (attempt {Retry}/{Max})",
                    retryCount, _options.MaxRetries);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }
    }

    private class EmbeddingRequest
    {
        [JsonPropertyName("input")]
        public required string[] Input { get; set; }

        [JsonPropertyName("model")]
        public required string Model { get; set; }
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = [];
    }

    private class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
