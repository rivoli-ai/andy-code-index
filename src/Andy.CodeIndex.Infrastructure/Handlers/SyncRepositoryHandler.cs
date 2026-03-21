using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class SyncRepositoryHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _options;
    private readonly ILogger<SyncRepositoryHandler> _logger;

    public TaskOperation Operation => TaskOperation.SyncRepository;

    public SyncRepositoryHandler(
        CodeIndexDbContext context, IGitService gitService,
        IOptions<IndexingOptions> options, ILogger<SyncRepositoryHandler> logger)
    {
        _context = context;
        _gitService = gitService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        await _gitService.FetchAsync(cloneDir, repo.PersonalAccessToken, ct);

        repo.LastSyncedAt = DateTime.UtcNow;
        repo.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Synced {Name}", repo.Name);
    }
}
