using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ApiKeyResolver : IApiKeyResolver
{
    private readonly CodeIndexDbContext _context;
    private readonly IEncryptionService _encryption;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly EnrichmentLlmOptions _llmOptions;
    private readonly ILogger<ApiKeyResolver> _logger;

    public ApiKeyResolver(
        CodeIndexDbContext context,
        IEncryptionService encryption,
        IOptions<EmbeddingOptions> embeddingOptions,
        IOptions<EnrichmentLlmOptions> llmOptions,
        ILogger<ApiKeyResolver> logger)
    {
        _context = context;
        _encryption = encryption;
        _embeddingOptions = embeddingOptions.Value;
        _llmOptions = llmOptions.Value;
        _logger = logger;
    }

    public async Task<(string? apiKey, string source)> ResolveEmbeddingKeyAsync(string? userId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            var userSettings = await _context.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId, ct);

            if (userSettings?.EmbeddingApiKey is not null)
            {
                var decrypted = _encryption.Decrypt(userSettings.EmbeddingApiKey);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    _logger.LogDebug("Using user-specific embedding key for {UserId}", userId);
                    return (decrypted, "user");
                }
            }
        }

        if (_embeddingOptions.IsConfigured)
        {
            _logger.LogDebug("Using system-level embedding key");
            return (_embeddingOptions.ApiKey!, "system");
        }

        // Tier 3: Fall back to any user's embedding key (for background tasks that don't have a user context)
        var anyUserSettings = await _context.UserSettings
            .FirstOrDefaultAsync(s => s.EmbeddingApiKey != null, ct);
        if (anyUserSettings?.EmbeddingApiKey is not null)
        {
            var decrypted = _encryption.Decrypt(anyUserSettings.EmbeddingApiKey);
            if (!string.IsNullOrEmpty(decrypted))
            {
                _logger.LogDebug("Using fallback embedding key from user {UserId}", anyUserSettings.UserId);
                return (decrypted, "user-fallback");
            }
        }

        return (null, "none");
    }

    public async Task<(string? apiKey, string model, string source)> ResolveLlmKeyAsync(string? userId = null, CancellationToken ct = default)
    {
        // Tier 1: User-specific LLM key
        if (!string.IsNullOrEmpty(userId))
        {
            var userSettings = await _context.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId, ct);

            if (userSettings?.LlmApiKey is not null)
            {
                var decrypted = _encryption.Decrypt(userSettings.LlmApiKey);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    _logger.LogDebug("Using user-specific LLM key for {UserId}", userId);
                    return (decrypted, _llmOptions.Model, "user");
                }
            }

            // User may also have set an embedding key that works for LLM (same provider)
            if (userSettings?.EmbeddingApiKey is not null)
            {
                var decrypted = _encryption.Decrypt(userSettings.EmbeddingApiKey);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    _logger.LogDebug("Using user embedding key as LLM fallback for {UserId}", userId);
                    return (decrypted, _llmOptions.Model, "user");
                }
            }
        }

        // Tier 2: System-level LLM key from appsettings
        if (_llmOptions.IsConfigured)
        {
            _logger.LogDebug("Using system-level LLM key");
            return (_llmOptions.ApiKey!, _llmOptions.Model, "system");
        }

        // Tier 3: Fall back to embedding key (same OpenAI account often works)
        if (_embeddingOptions.IsConfigured)
        {
            _logger.LogDebug("Using embedding key as LLM fallback");
            return (_embeddingOptions.ApiKey!, _llmOptions.Model, "system");
        }

        // Tier 4: Fall back to any user's key (for background tasks without user context)
        var anyUser = await _context.UserSettings
            .FirstOrDefaultAsync(s => s.LlmApiKey != null || s.EmbeddingApiKey != null, ct);
        if (anyUser is not null)
        {
            var key = anyUser.LlmApiKey ?? anyUser.EmbeddingApiKey;
            if (key is not null)
            {
                var decrypted = _encryption.Decrypt(key);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    _logger.LogDebug("Using fallback LLM key from user {UserId}", anyUser.UserId);
                    return (decrypted, _llmOptions.Model, "user-fallback");
                }
            }
        }

        return (null, _llmOptions.Model, "none");
    }
}
