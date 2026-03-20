using Andy.CodeIndex.Domain.Entities;

namespace Andy.CodeIndex.Application.Interfaces;

public interface ICommitRepository : IRepository<Commit>
{
    Task<Commit?> GetByShaAsync(Guid repositoryId, string sha, CancellationToken ct = default);
    Task<List<Commit>> GetByRepositoryAsync(Guid repositoryId, int offset = 0, int limit = 50, CancellationToken ct = default);
    Task<Commit?> GetLatestIndexedAsync(Guid repositoryId, CancellationToken ct = default);
}
