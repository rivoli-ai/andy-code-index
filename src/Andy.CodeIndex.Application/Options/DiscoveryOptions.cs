namespace Andy.CodeIndex.Application.Options;

public class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public GitHubDiscoveryOptions? GitHub { get; set; }
    public AzureDevOpsDiscoveryOptions? AzureDevOps { get; set; }
    public List<SeedRepository>? SeedRepositories { get; set; }
}

public class GitHubDiscoveryOptions
{
    public string? Organization { get; set; }
    public string? Pat { get; set; }
    public bool ExcludeArchived { get; set; } = true;
    public bool ExcludeForks { get; set; } = true;
}

public class AzureDevOpsDiscoveryOptions
{
    public string? Organization { get; set; }
    public string? Project { get; set; }
    public string? Pat { get; set; }
}

public class SeedRepository
{
    public required string Url { get; set; }
    public string? Pat { get; set; }
}
