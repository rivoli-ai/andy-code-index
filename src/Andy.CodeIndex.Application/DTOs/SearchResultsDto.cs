namespace Andy.CodeIndex.Application.DTOs;

public class SearchResultsDto
{
    public List<SearchResultItem> Results { get; set; } = [];
    public int TotalCount { get; set; }
    public string SearchMode { get; set; } = "hybrid";
    public long DurationMs { get; set; }
}

public class SearchResultItem
{
    public Guid EnrichmentId { get; set; }
    public required string Content { get; set; }
    public double Score { get; set; }
    public string? FilePath { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Language { get; set; }
    public Guid RepositoryId { get; set; }
    public string? RepositoryName { get; set; }
    public string? CommitSha { get; set; }
}
