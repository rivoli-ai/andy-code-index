using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IEnrichmentGeneratorService
{
    Task<List<EnrichmentDto>> QueryAsync(
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

    Task<EnrichmentDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task DeleteByRepositoryAndTypeAsync(
        Guid repositoryId, EnrichmentType type, Guid? commitId = null, CancellationToken ct = default);

    Task<Dictionary<string, int>> GetCountsBySubtypeAsync(
        EnrichmentType? type = null, Guid? repositoryId = null, CancellationToken ct = default);
}
