using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Discovery;

public class SeedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscoveryOptions _options;
    private readonly ILogger<SeedService> _logger;

    public SeedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DiscoveryOptions> options,
        ILogger<SeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_options.SeedRepositories is null || _options.SeedRepositories.Count == 0)
        {
            _logger.LogDebug("No seed repositories configured");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repoService = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        _logger.LogInformation("Seeding {Count} repositories", _options.SeedRepositories.Count);

        foreach (var seed in _options.SeedRepositories)
        {
            try
            {
                await repoService.AddAsync(new CreateRepositoryRequest
                {
                    Url = seed.Url,
                    PersonalAccessToken = seed.Pat
                }, ct);
                _logger.LogInformation("Seeded repository: {Url}", seed.Url);
            }
            catch (InvalidOperationException)
            {
                _logger.LogDebug("Repository already exists: {Url}", seed.Url);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Default rivoli-ai seed repositories.</summary>
    public static readonly List<SeedRepository> RivoliAiRepos =
    [
        new() { Url = "https://github.com/rivoli-ai/andy-docs" },
        new() { Url = "https://github.com/rivoli-ai/andy-auth" },
        new() { Url = "https://github.com/rivoli-ai/andy-rbac" },
        new() { Url = "https://github.com/rivoli-ai/andy-code-index" },
    ];
}
