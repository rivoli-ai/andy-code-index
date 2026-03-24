using Andy.CodeIndex.Application.DTOs;

namespace Andy.CodeIndex.Application.Interfaces;

public interface ISearchService
{
    Task<SearchResultsDto> SemanticSearchAsync(string query, SearchFilter? filter = null, int limit = 10, CancellationToken ct = default);
    Task<SearchResultsDto> KeywordSearchAsync(string keywords, SearchFilter? filter = null, int limit = 10, CancellationToken ct = default);
    Task<SearchResultsDto> HybridSearchAsync(string query, SearchFilter? filter = null, int limit = 10, CancellationToken ct = default);
    Task<SearchFilterOptions> GetFilterOptionsAsync(CancellationToken ct = default);
}

public class SearchFilter
{
    public List<string>? Languages { get; set; }
    public List<Guid>? RepositoryIds { get; set; }
    public List<string>? Authors { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public string? CommitSha { get; set; }
    public string? FilePath { get; set; }
}

public class SearchFilterOptions
{
    public List<FilterOption> Repositories { get; set; } = [];
    public List<string> Languages { get; set; } = [];
}

public class FilterOption
{
    public required string Id { get; set; }
    public required string Name { get; set; }
}
