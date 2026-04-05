using Andy.CodeIndex.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ApiKeyHealthStatus
{
    public bool LlmKeyValid { get; set; }
    public bool EmbeddingKeyValid { get; set; }
    public string? LlmError { get; set; }
    public string? EmbeddingError { get; set; }
    public DateTime LastChecked { get; set; }
}

public class ApiKeyHealthService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApiKeyHealthStatus _status;
    private readonly ILogger<ApiKeyHealthService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    public ApiKeyHealthService(
        IServiceScopeFactory scopeFactory,
        ApiKeyHealthStatus status,
        ILogger<ApiKeyHealthService> logger)
    {
        _scopeFactory = scopeFactory;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("API key health check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckKeysAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during API key health check");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("API key health check service stopped");
    }

    internal async Task CheckKeysAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IApiKeyResolver>();

        try
        {
            var (apiKey, _, _, _) = await resolver.ResolveLlmKeyAsync(ct: ct);
            _status.LlmKeyValid = !string.IsNullOrEmpty(apiKey);
            _status.LlmError = string.IsNullOrEmpty(apiKey) ? "No LLM API key configured" : null;
        }
        catch (Exception ex)
        {
            _status.LlmKeyValid = false;
            _status.LlmError = ex.Message;
        }

        try
        {
            var (apiKey, _, _, _) = await resolver.ResolveEmbeddingKeyAsync(ct: ct);
            _status.EmbeddingKeyValid = !string.IsNullOrEmpty(apiKey);
            _status.EmbeddingError = string.IsNullOrEmpty(apiKey) ? "No embedding API key configured" : null;
        }
        catch (Exception ex)
        {
            _status.EmbeddingKeyValid = false;
            _status.EmbeddingError = ex.Message;
        }

        _status.LastChecked = DateTime.UtcNow;

        _logger.LogInformation(
            "API key health check complete: LLM={LlmValid}, Embedding={EmbeddingValid}",
            _status.LlmKeyValid, _status.EmbeddingKeyValid);
    }
}
