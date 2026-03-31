using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CloneRepositoryHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _options;
    private readonly ILogger<CloneRepositoryHandler> _logger;

    public TaskOperation Operation => TaskOperation.CloneRepository;

    public CloneRepositoryHandler(
        CodeIndexDbContext context,
        IGitService gitService,
        IOptions<IndexingOptions> options,
        ILogger<CloneRepositoryHandler> logger)
    {
        _context = context;
        _gitService = gitService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var trackedTask = await _context.IndexingTasks.FindAsync([task.Id], ct);
        if (trackedTask is not null)
        {
            trackedTask.ProgressMessage = "Cloning repository...";
            trackedTask.Progress = 0;
            await _context.SaveChangesAsync(ct);
        }

        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        try
        {
            repo.Status = "cloning";
            repo.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
            _logger.LogInformation("Cloning {Url} to {Dir}", repo.Url, cloneDir);

            await _gitService.CloneAsync(repo.Url, cloneDir, repo.PersonalAccessToken, ct);
            _logger.LogInformation("Clone completed for {Name}", repo.Name);

            // Update branches
            var branches = await _gitService.GetBranchesAsync(cloneDir, ct);
            _logger.LogInformation("Found {Count} branches for {Name}", branches.Count, repo.Name);

            // Add branches and tags via DbContext.AddRangeAsync to ensure INSERT
            var branchEntities = branches.Select(b => new Branch
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Name = b.Name,
                HeadCommitSha = b.HeadCommitSha,
                IsDefault = b.IsDefault,
                CreatedAt = DateTime.UtcNow
            }).ToList();
            await _context.Branches.AddRangeAsync(branchEntities, ct);

            var tags = await _gitService.GetTagsAsync(cloneDir, ct);
            var tagEntities = tags.Select(t => new Tag
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Name = t.Name,
                CommitSha = t.CommitSha,
                CreatedAt = DateTime.UtcNow
            }).ToList();
            await _context.Tags.AddRangeAsync(tagEntities, ct);

            if (branches.Any(b => b.IsDefault))
                repo.DefaultBranch = branches.First(b => b.IsDefault).Name;

            repo.Status = "cloned";
            repo.LastSyncedAt = DateTime.UtcNow;
            repo.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Cloned {Name}: {BranchCount} branches, {TagCount} tags",
                repo.Name, branches.Count, tags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clone failed for {Name}", repo.Name);

            // Reset repo status so it can be retried
            try
            {
                repo.Status = "error";
                repo.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to set error status for {Name}", repo.Name);
            }

            throw; // Re-throw so BackgroundWorkerService marks the task as Failed
        }
    }
}
