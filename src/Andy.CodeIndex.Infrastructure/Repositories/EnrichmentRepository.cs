using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Repositories;

public class EnrichmentRepository : RepositoryBase<Enrichment>, IEnrichmentRepository
{
    public EnrichmentRepository(CodeIndexDbContext context) : base(context) { }

    public async Task<List<Enrichment>> QueryAsync(
        EnrichmentType? type = null,
        EnrichmentSubtype? subtype = null,
        Guid? repositoryId = null,
        Guid? commitId = null,
        string? language = null,
        string? filePath = null,
        int offset = 0,
        int limit = 50,
        CancellationToken ct = default)
    {
        return await BuildQuery(type, subtype, repositoryId, commitId, language, filePath)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> QueryCountAsync(
        EnrichmentType? type = null,
        EnrichmentSubtype? subtype = null,
        Guid? repositoryId = null,
        Guid? commitId = null,
        string? language = null,
        string? filePath = null,
        CancellationToken ct = default)
    {
        return await BuildQuery(type, subtype, repositoryId, commitId, language, filePath)
            .CountAsync(ct);
    }

    public async Task<List<Enrichment>> GetByRepositoryAndSubtypeAsync(
        Guid repositoryId, EnrichmentSubtype subtype, string? commitSha = null, CancellationToken ct = default)
    {
        var query = DbSet.Where(e => e.RepositoryId == repositoryId && e.Subtype == subtype);

        if (commitSha is not null)
            query = query.Where(e => e.Commit != null && e.Commit.Sha == commitSha);

        return await query.ToListAsync(ct);
    }

    public async Task DeleteByRepositoryAndTypeAsync(
        Guid repositoryId, EnrichmentType type, Guid? commitId = null, CancellationToken ct = default)
    {
        var query = DbSet.Where(e => e.RepositoryId == repositoryId && e.Type == type);

        if (commitId.HasValue)
            query = query.Where(e => e.CommitId == commitId.Value);

        var enrichments = await query.ToListAsync(ct);
        DbSet.RemoveRange(enrichments);
        await Context.SaveChangesAsync(ct);
    }

    private IQueryable<Enrichment> BuildQuery(
        EnrichmentType? type,
        EnrichmentSubtype? subtype,
        Guid? repositoryId,
        Guid? commitId,
        string? language,
        string? filePath)
    {
        IQueryable<Enrichment> query = DbSet;

        if (type.HasValue) query = query.Where(e => e.Type == type.Value);
        if (subtype.HasValue) query = query.Where(e => e.Subtype == subtype.Value);
        if (repositoryId.HasValue) query = query.Where(e => e.RepositoryId == repositoryId.Value);
        if (commitId.HasValue) query = query.Where(e => e.CommitId == commitId.Value);
        if (language is not null) query = query.Where(e => e.Language == language);
        if (filePath is not null) query = query.Where(e => e.FilePath == filePath);

        return query;
    }
}
