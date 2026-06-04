using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.DTOs;

public class RepositoryDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? Organization { get; set; }
    public GitProvider Provider { get; set; }
    public string? DefaultBranch { get; set; }
    public string? LastIndexedCommitSha { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int? SyncIntervalMinutes { get; set; }
    public required string Status { get; set; }
    public FileFilterOverridesDto? FileFilterOverrides { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public RepositoryStatsDto? Stats { get; set; }
    public List<BranchDto>? Branches { get; set; }
    public List<TagDto>? Tags { get; set; }
}

public class RepositoryStatsDto
{
    public int CommitCount { get; set; }
    public int FileCount { get; set; }
    public int EnrichmentCount { get; set; }
    public long StorageSizeBytes { get; set; }
    public int EmbeddingCount { get; set; }
    public int PendingTaskCount { get; set; }
    public bool HasEmbeddings { get; set; }
    public bool NeedsAttention { get; set; }
    public string? AttentionReason { get; set; }
    public bool HasInsights { get; set; }
}

public class StorageStatsDto
{
    public int TotalEnrichments { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<StorageByTypeDto> ByType { get; set; } = [];
}

public class StorageByTypeDto
{
    public required string Type { get; set; }
    public int Count { get; set; }
    public long SizeBytes { get; set; }
}

public class BranchDto
{
    public required string Name { get; set; }
    public string? HeadCommitSha { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Per-branch indexing status. Returned by
/// GET /api/v1/repositories/{id}/branches/{branch}/status.
/// Consumers MUST key state on branch name rather than synthesising a
/// default branch — this endpoint retires the synthetic-default-branch hazard.
/// </summary>
public class BranchIndexingStatusDto
{
    /// <summary>Branch name (exact, URL-decoded).</summary>
    public required string Branch { get; set; }

    /// <summary>
    /// Repository-level indexing status at the time of the request.
    /// One of: <c>pending</c>, <c>cloning</c>, <c>indexing</c>, <c>indexed</c>, <c>error</c>.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Commit SHA of HEAD on this branch as of the last completed indexing run.
    /// <c>null</c> if no indexing run has completed for this branch yet.
    /// </summary>
    public string? LastIndexedCommitSha { get; set; }

    /// <summary>
    /// Current HEAD SHA on this branch (from the git clone, if available).
    /// May differ from <see cref="LastIndexedCommitSha"/> when commits have been
    /// pushed since the last index run.
    /// </summary>
    public string? HeadCommitSha { get; set; }

    /// <summary>
    /// Progress percentage (0–100) of an active indexing task for this branch,
    /// or <c>null</c> if no task is currently running.
    /// </summary>
    public int? Progress { get; set; }
}

public class TagDto
{
    public required string Name { get; set; }
    public required string CommitSha { get; set; }
}
