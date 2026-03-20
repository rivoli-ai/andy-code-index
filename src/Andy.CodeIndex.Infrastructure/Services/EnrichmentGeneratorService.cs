using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Infrastructure.Services;

public class EnrichmentGeneratorService : IEnrichmentGeneratorService
{
    private readonly IEnrichmentRepository _enrichmentRepo;

    public EnrichmentGeneratorService(IEnrichmentRepository enrichmentRepo)
    {
        _enrichmentRepo = enrichmentRepo;
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

        return enrichments.Select(MapToDto).ToList();
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
        return enrichment is null ? null : MapToDto(enrichment);
    }

    public async Task DeleteByRepositoryAndTypeAsync(
        Guid repositoryId, EnrichmentType type, Guid? commitId, CancellationToken ct)
    {
        await _enrichmentRepo.DeleteByRepositoryAndTypeAsync(repositoryId, type, commitId, ct);
    }

    private static EnrichmentDto MapToDto(Enrichment e) => new()
    {
        Id = e.Id,
        RepositoryId = e.RepositoryId,
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
