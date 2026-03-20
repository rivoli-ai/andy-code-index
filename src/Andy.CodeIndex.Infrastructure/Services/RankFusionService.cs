namespace Andy.CodeIndex.Infrastructure.Services;

public class RankFusionService
{
    private const int DefaultK = 60;

    public List<FusedResult> Fuse(List<RankedResultSet> inputs, int k = DefaultK)
    {
        var scores = new Dictionary<Guid, double>();
        var metadata = new Dictionary<Guid, FusedResult>();

        foreach (var resultSet in inputs)
        {
            for (var rank = 0; rank < resultSet.Results.Count; rank++)
            {
                var item = resultSet.Results[rank];
                var rrf = 1.0 / (k + rank); // 0-based rank, matching Kodit

                if (!scores.ContainsKey(item.EnrichmentId))
                {
                    scores[item.EnrichmentId] = 0;
                    metadata[item.EnrichmentId] = item;
                }
                scores[item.EnrichmentId] += rrf;
            }
        }

        return metadata.Values
            .Select(r => r with { FusedScore = scores[r.EnrichmentId] })
            .OrderByDescending(r => r.FusedScore)
            .ToList();
    }
}

public record FusedResult
{
    public Guid EnrichmentId { get; init; }
    public required string Content { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? Language { get; init; }
    public Guid RepositoryId { get; init; }
    public string? RepositoryName { get; init; }
    public double OriginalScore { get; init; }
    public double FusedScore { get; init; }
}

public class RankedResultSet
{
    public required string Source { get; set; } // "semantic_code", "semantic_text", "bm25"
    public List<FusedResult> Results { get; set; } = [];
}
