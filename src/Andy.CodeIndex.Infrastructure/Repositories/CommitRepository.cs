using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Repositories;

public class CommitRepository : RepositoryBase<Commit>, ICommitRepository
{
    public CommitRepository(CodeIndexDbContext context) : base(context) { }

    public async Task<Commit?> GetByShaAsync(Guid repositoryId, string sha, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(c => c.RepositoryId == repositoryId && c.Sha == sha, ct);

    public async Task<List<Commit>> GetByRepositoryAsync(Guid repositoryId, int offset = 0, int limit = 50, CancellationToken ct = default)
        => await DbSet
            .Where(c => c.RepositoryId == repositoryId)
            .OrderByDescending(c => c.CommittedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<Commit?> GetLatestIndexedAsync(Guid repositoryId, CancellationToken ct = default)
        => await DbSet
            .Where(c => c.RepositoryId == repositoryId && c.IsIndexed)
            .OrderByDescending(c => c.CommittedAt)
            .FirstOrDefaultAsync(ct);
}
