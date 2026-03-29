using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Domain.Entities;

public class Repository
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? CloneUrl { get; set; }
    public GitProvider Provider { get; set; }
    public string? DefaultBranch { get; set; }
    public string? PersonalAccessToken { get; set; }
    public string? LastIndexedCommitSha { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int? SyncIntervalMinutes { get; set; } // null=default, 0=manual only, 15/30/60/120/360/720/1440
    public string Status { get; set; } = "pending"; // pending, cloning, indexing, indexed, error
    public string? FileFilterOverrides { get; set; } // JSON string for per-repo filter overrides
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Commit> Commits { get; set; } = new List<Commit>();
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<Enrichment> Enrichments { get; set; } = new List<Enrichment>();
    public ICollection<IndexingTask> IndexingTasks { get; set; } = new List<IndexingTask>();
}
