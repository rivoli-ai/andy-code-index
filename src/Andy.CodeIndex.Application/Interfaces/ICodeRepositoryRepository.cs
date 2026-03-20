using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface ICodeRepositoryRepository : IRepository<Repository>
{
    Task<Repository?> GetByUrlAsync(string url, CancellationToken ct = default);
    Task<Repository?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<Repository?> GetWithBranchesAndTagsAsync(Guid id, CancellationToken ct = default);
    Task<List<Repository>> GetByStatusAsync(string status, CancellationToken ct = default);
    Task<List<Repository>> GetByProviderAsync(GitProvider provider, CancellationToken ct = default);
}
