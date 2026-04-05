using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IRepositoryService
{
    Task<RepositoryDto> AddAsync(CreateRepositoryRequest request, CancellationToken ct = default);
    Task<RepositoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RepositoryDto?> GetDetailsByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<RepositoryDto>> ListAsync(GitProvider? provider = null, string? status = null, CancellationToken ct = default);
    Task<RepositoryDto> UpdateAsync(Guid id, UpdateRepositoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SyncAsync(Guid id, CancellationToken ct = default);
    Task WipeEnrichmentsAsync(Guid id, CancellationToken ct = default);
    Task<RepositoryDto?> FindByUrlAsync(string url, CancellationToken ct = default);
    Task<StorageStatsDto> GetStorageStatsAsync(Guid repositoryId, CancellationToken ct = default);
    Task<StorageStatsDto> GetGlobalStorageStatsAsync(CancellationToken ct = default);
}
