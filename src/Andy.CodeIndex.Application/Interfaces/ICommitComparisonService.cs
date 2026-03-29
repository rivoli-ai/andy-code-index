using Andy.CodeIndex.Application.DTOs;

namespace Andy.CodeIndex.Application.Interfaces;

public interface ICommitComparisonService
{
    Task<CommitComparisonDto?> CompareAsync(Guid repositoryId, string fromSha, string toSha, CancellationToken ct = default);
}
