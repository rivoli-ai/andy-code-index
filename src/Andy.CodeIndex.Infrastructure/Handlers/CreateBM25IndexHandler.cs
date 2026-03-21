using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CreateBM25IndexHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly ILogger<CreateBM25IndexHandler> _logger;

    public TaskOperation Operation => TaskOperation.CreateBM25Index;

    public CreateBM25IndexHandler(CodeIndexDbContext context, ILogger<CreateBM25IndexHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        // BM25 index is maintained automatically via the tsvector computed column on Enrichments.
        // This handler just verifies enrichments exist and logs the count.
        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var enrichmentCount = _context.Enrichments.Count(e => e.RepositoryId == repo.Id);
        _logger.LogInformation("BM25 index ready for {Name}: {Count} enrichments indexed via tsvector",
            repo.Name, enrichmentCount);
    }
}
