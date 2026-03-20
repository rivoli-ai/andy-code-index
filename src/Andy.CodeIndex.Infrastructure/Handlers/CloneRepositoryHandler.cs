using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CloneRepositoryHandler : ITaskHandler
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _options;
    private readonly ILogger<CloneRepositoryHandler> _logger;

    public TaskOperation Operation => TaskOperation.CloneRepository;

    public CloneRepositoryHandler(
        ICodeRepositoryRepository repoRepo,
        IGitService gitService,
        IOptions<IndexingOptions> options,
        ILogger<CloneRepositoryHandler> logger)
    {
        _repoRepo = repoRepo;
        _gitService = gitService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(task.RepositoryId, ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        repo.Status = "cloning";
        _repoRepo.Update(repo);
        await _repoRepo.SaveChangesAsync(ct);

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        await _gitService.CloneAsync(repo.Url, cloneDir, repo.PersonalAccessToken, ct);

        // Update branches and tags
        var branches = await _gitService.GetBranchesAsync(cloneDir, ct);
        foreach (var b in branches)
        {
            repo.Branches.Add(new Branch
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Name = b.Name,
                HeadCommitSha = b.HeadCommitSha,
                IsDefault = b.IsDefault,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (branches.Any(b => b.IsDefault))
            repo.DefaultBranch = branches.First(b => b.IsDefault).Name;

        repo.Status = "cloned";
        repo.UpdatedAt = DateTime.UtcNow;
        _repoRepo.Update(repo);
        await _repoRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Cloned {Name} with {BranchCount} branches", repo.Name, branches.Count);
    }
}
