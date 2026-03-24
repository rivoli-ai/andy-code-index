using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Services;

public class EnrichmentGeneratorService : IEnrichmentGeneratorService
{
    private readonly IEnrichmentRepository _enrichmentRepo;
    private readonly CodeIndexDbContext _context;

    public EnrichmentGeneratorService(IEnrichmentRepository enrichmentRepo, CodeIndexDbContext context)
    {
        _enrichmentRepo = enrichmentRepo;
        _context = context;
    }

    public async Task<List<EnrichmentDto>> QueryAsync(
        EnrichmentType? type, EnrichmentSubtype? subtype,
        Guid? repositoryId, Guid? commitId,
        string? language, string? filePath,
        int offset, int limit,
        CancellationToken ct)
    {
        var enrichments = await _enrichmentRepo.QueryAsync(
            type, subtype, repositoryId, commitId, language, filePath, offset, limit, ct);

        // Build repo name lookup
        var repoIds = enrichments.Select(e => e.RepositoryId).Distinct().ToList();
        var repoNames = await _context.Repositories
            .Where(r => repoIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return enrichments.Select(e => MapToDto(e, repoNames)).ToList();
    }

    public async Task<int> QueryCountAsync(
        EnrichmentType? type, EnrichmentSubtype? subtype,
        Guid? repositoryId, Guid? commitId,
        string? language, string? filePath,
        CancellationToken ct)
    {
        return await _enrichmentRepo.QueryCountAsync(
            type, subtype, repositoryId, commitId, language, filePath, ct);
    }

    public async Task<EnrichmentDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var enrichment = await _enrichmentRepo.GetByIdAsync(id, ct);
        if (enrichment is null) return null;

        var repo = await _context.Repositories.FindAsync([enrichment.RepositoryId], ct);
        var names = new Dictionary<Guid, string>();
        if (repo is not null) names[repo.Id] = repo.Name;

        return MapToDto(enrichment, names);
    }

    public async Task DeleteByRepositoryAndTypeAsync(
        Guid repositoryId, EnrichmentType type, Guid? commitId, CancellationToken ct)
    {
        await _enrichmentRepo.DeleteByRepositoryAndTypeAsync(repositoryId, type, commitId, ct);
    }

    public async Task<Dictionary<string, int>> GetCountsBySubtypeAsync(
        EnrichmentType? type = null, Guid? repositoryId = null, CancellationToken ct = default)
    {
        return await _enrichmentRepo.QueryCountsBySubtypeAsync(type, repositoryId, ct);
    }

    private static EnrichmentDto MapToDto(Enrichment e, Dictionary<Guid, string> repoNames) => new()
    {
        Id = e.Id,
        RepositoryId = e.RepositoryId,
        RepositoryName = repoNames.TryGetValue(e.RepositoryId, out var name) ? name : null,
        CommitId = e.CommitId,
        Type = e.Type,
        Subtype = e.Subtype,
        Title = e.Title,
        Content = e.Content,
        FilePath = e.FilePath,
        StartLine = e.StartLine,
        EndLine = e.EndLine,
        Language = e.Language,
        CreatedAt = e.CreatedAt
    };
}
