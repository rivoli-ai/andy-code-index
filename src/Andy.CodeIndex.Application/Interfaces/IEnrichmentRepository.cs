using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IEnrichmentRepository : IRepository<Enrichment>
{
    Task<List<Enrichment>> QueryAsync(
        EnrichmentType? type = null,
        EnrichmentSubtype? subtype = null,
        Guid? repositoryId = null,
        Guid? commitId = null,
        string? language = null,
        string? filePath = null,
        int offset = 0,
        int limit = 50,
        CancellationToken ct = default);

    Task<int> QueryCountAsync(
        EnrichmentType? type = null,
        EnrichmentSubtype? subtype = null,
        Guid? repositoryId = null,
        Guid? commitId = null,
        string? language = null,
        string? filePath = null,
        CancellationToken ct = default);

    Task<List<Enrichment>> GetByRepositoryAndSubtypeAsync(
        Guid repositoryId, EnrichmentSubtype subtype, string? commitSha = null, CancellationToken ct = default);

    Task DeleteByRepositoryAndTypeAsync(
        Guid repositoryId, EnrichmentType type, Guid? commitId = null, CancellationToken ct = default);
}
