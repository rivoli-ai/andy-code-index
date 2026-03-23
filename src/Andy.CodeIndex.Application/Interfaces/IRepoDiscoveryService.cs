namespace Andy.CodeIndex.Application.Interfaces;

public interface IRepoDiscoveryService
{
    Task<List<DiscoveredRepo>> DiscoverGitHubAsync(string organization, string? pat = null, bool excludeArchived = true, bool excludeForks = true, CancellationToken ct = default);
    Task<List<DiscoveredRepo>> DiscoverAzureDevOpsAsync(string organization, string? project = null, string? pat = null, CancellationToken ct = default);
}

public class DiscoveredRepo
{
    public required string Name { get; set; }
    public required string FullName { get; set; }
    public required string CloneUrl { get; set; }
    public required string Provider { get; set; }
    public string? DefaultBranch { get; set; }
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
    public bool IsFork { get; set; }
    public bool IsDisabled { get; set; }
    public bool AlreadyTracked { get; set; }
}
