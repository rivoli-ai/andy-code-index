using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Repositories;

public class CodeRepositoryRepository : RepositoryBase<Repository>, ICodeRepositoryRepository
{
    public CodeRepositoryRepository(CodeIndexDbContext context) : base(context) { }

    public async Task<Repository?> GetByUrlAsync(string url, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(r => r.Url == url, ct);

    public async Task<Repository?> GetByNameAsync(string name, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task<Repository?> GetWithBranchesAndTagsAsync(Guid id, CancellationToken ct = default)
        => await DbSet
            .Include(r => r.Branches)
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<List<Repository>> GetByStatusAsync(string status, CancellationToken ct = default)
        => await DbSet.Where(r => r.Status == status).ToListAsync(ct);

    public async Task<List<Repository>> GetByProviderAsync(GitProvider provider, CancellationToken ct = default)
        => await DbSet.Where(r => r.Provider == provider).ToListAsync(ct);
}
