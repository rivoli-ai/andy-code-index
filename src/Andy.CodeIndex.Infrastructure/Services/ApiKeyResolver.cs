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
    private readonly EmbeddingOptions _options;
    private readonly ILogger<ApiKeyResolver> _logger;

    public ApiKeyResolver(
        CodeIndexDbContext context,
        IEncryptionService encryption,
        IOptions<EmbeddingOptions> options,
        ILogger<ApiKeyResolver> logger)
    {
        _context = context;
        _encryption = encryption;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(string? apiKey, string source)> ResolveEmbeddingKeyAsync(string? userId = null, CancellationToken ct = default)
    {
        // Tier 1: User-specific key
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

        // Tier 2: System-level key from appsettings
        if (_options.IsConfigured)
        {
            _logger.LogDebug("Using system-level embedding key from configuration");
            return (_options.ApiKey!, "system");
        }

        _logger.LogDebug("No embedding key available");
        return (null, "none");
    }
}
