using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.DTOs;

public class RepositoryDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
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
    public int EmbeddingCount { get; set; }
    public int PendingTaskCount { get; set; }
    public bool HasEmbeddings { get; set; }
}

public class BranchDto
{
    public required string Name { get; set; }
    public string? HeadCommitSha { get; set; }
    public bool IsDefault { get; set; }
}

public class TagDto
{
    public required string Name { get; set; }
    public required string CommitSha { get; set; }
}
