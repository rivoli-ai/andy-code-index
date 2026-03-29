using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class ScanCommitHandler : ITaskHandler
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly ICommitRepository _commitRepo;
    private readonly IGitService _gitService;
    private readonly CodeIndexDbContext _context;
    private readonly IndexingOptions _options;
    private readonly ILogger<ScanCommitHandler> _logger;

    public TaskOperation Operation => TaskOperation.ScanCommit;

    public ScanCommitHandler(
        ICodeRepositoryRepository repoRepo,
        ICommitRepository commitRepo,
        IGitService gitService,
        CodeIndexDbContext context,
        IOptions<IndexingOptions> options,
        ILogger<ScanCommitHandler> logger)
    {
        _repoRepo = repoRepo;
        _commitRepo = commitRepo;
        _gitService = gitService;
        _context = context;
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
        Commit? latestNewCommit = null;

        foreach (var c in commits)
        {
            var exists = await _commitRepo.ExistsAsync(
                x => x.RepositoryId == repo.Id && x.Sha == c.Sha, ct);
            if (exists) continue;

            var commit = new Commit
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Sha = c.Sha,
                Message = c.Message,
                AuthorName = c.AuthorName,
                AuthorEmail = c.AuthorEmail,
                CommittedAt = c.CommittedAt,
                CreatedAt = DateTime.UtcNow
            };
            await _commitRepo.AddAsync(commit, ct);
            newCount++;

            // Track the latest commit (first in the list from git log)
            latestNewCommit ??= commit;
        }

        await _commitRepo.SaveChangesAsync(ct);

        // Create RepositoryFile records for the latest commit
        if (latestNewCommit != null)
        {
            var files = await _gitService.ListFilesAsync(cloneDir, latestNewCommit.Sha, ct: ct);
            foreach (var file in files)
            {
                _context.RepositoryFiles.Add(new RepositoryFile
                {
                    Id = Guid.NewGuid(),
                    CommitId = latestNewCommit.Id,
                    Path = file.Path,
                    Language = file.Language,
                    Size = file.Size,
                    Hash = file.Hash,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Created {FileCount} repository file records for commit {Sha}",
                files.Count, latestNewCommit.Sha[..Math.Min(8, latestNewCommit.Sha.Length)]);
        }

        _logger.LogInformation("Scanned {New} new commits for {Name}", newCount, repo.Name);
    }
}
