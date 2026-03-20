using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class ScanCommitHandler : ITaskHandler
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly ICommitRepository _commitRepo;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _options;
    private readonly ILogger<ScanCommitHandler> _logger;

    public TaskOperation Operation => TaskOperation.ScanCommit;

    public ScanCommitHandler(
        ICodeRepositoryRepository repoRepo,
        ICommitRepository commitRepo,
        IGitService gitService,
        IOptions<IndexingOptions> options,
        ILogger<ScanCommitHandler> logger)
    {
        _repoRepo = repoRepo;
        _commitRepo = commitRepo;
        _gitService = gitService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(task.RepositoryId, ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        var commits = await _gitService.GetCommitsAsync(cloneDir, sinceSha: repo.LastIndexedCommitSha, ct: ct);

        var newCount = 0;
        foreach (var c in commits)
        {
            var exists = await _commitRepo.ExistsAsync(
                x => x.RepositoryId == repo.Id && x.Sha == c.Sha, ct);
            if (exists) continue;

            await _commitRepo.AddAsync(new Commit
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Sha = c.Sha,
                Message = c.Message,
                AuthorName = c.AuthorName,
                AuthorEmail = c.AuthorEmail,
                CommittedAt = c.CommittedAt,
                CreatedAt = DateTime.UtcNow
            }, ct);
            newCount++;
        }

        await _commitRepo.SaveChangesAsync(ct);
        _logger.LogInformation("Scanned {New} new commits for {Name}", newCount, repo.Name);
    }
}
