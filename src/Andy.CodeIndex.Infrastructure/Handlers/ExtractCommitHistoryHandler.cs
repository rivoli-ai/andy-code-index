using System.Text;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class ExtractCommitHistoryHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _options;
    private readonly ILogger<ExtractCommitHistoryHandler> _logger;

    public TaskOperation Operation => TaskOperation.ExtractCommitHistory;

    public ExtractCommitHistoryHandler(
        CodeIndexDbContext context, IGitService gitService,
        IOptions<IndexingOptions> options, ILogger<ExtractCommitHistoryHandler> logger)
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

        var commits = await _gitService.GetCommitsAsync(cloneDir, limit: 200, ct: ct);
        var tags = await _gitService.GetTagsAsync(cloneDir, ct);

        // Delete existing commit history enrichments
        var existing = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.CommitHistory)
            .ToListAsync(ct);
        _context.Enrichments.RemoveRange(existing);

        var md = new StringBuilder();
        md.AppendLine($"# Commit History for {repo.Name}");
        md.AppendLine();

        if (tags.Count > 0)
        {
            md.AppendLine($"## Tags ({tags.Count})");
            md.AppendLine();
            md.AppendLine("| Tag | Commit |");
            md.AppendLine("|-----|--------|");
            foreach (var tag in tags.OrderByDescending(t => t.Name))
            {
                md.AppendLine($"| {tag.Name} | `{tag.CommitSha[..Math.Min(8, tag.CommitSha.Length)]}` |");
            }
            md.AppendLine();
        }

        md.AppendLine($"## Recent Commits ({commits.Count})");
        md.AppendLine();
        md.AppendLine("| Date | Author | Message | SHA |");
        md.AppendLine("|------|--------|---------|-----|");
        foreach (var commit in commits)
        {
            var message = commit.Message.Split('\n')[0];
            if (message.Length > 80) message = message[..77] + "...";
            md.AppendLine($"| {commit.CommittedAt:yyyy-MM-dd} | {commit.AuthorName} | {message} | `{commit.Sha[..Math.Min(8, commit.Sha.Length)]}` |");
        }

        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Type = EnrichmentType.History,
            Subtype = EnrichmentSubtype.CommitHistory,
            Title = $"Commit History ({commits.Count} commits, {tags.Count} tags)",
            Content = md.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Extracted commit history for {Name}: {Commits} commits, {Tags} tags",
            repo.Name, commits.Count, tags.Count);
    }
}
