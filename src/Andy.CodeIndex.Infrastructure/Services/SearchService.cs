using System.Diagnostics;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Services;

public class SearchService : ISearchService
{
    private readonly CodeIndexDbContext _context;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly RankFusionService _fusionService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        CodeIndexDbContext context,
        IEmbeddingProvider embeddingProvider,
        RankFusionService fusionService,
        ILogger<SearchService> logger)
    {
        _context = context;
        _embeddingProvider = embeddingProvider;
        _fusionService = fusionService;
        _logger = logger;
    }

    public async Task<SearchResultsDto> SemanticSearchAsync(string query, SearchFilter? filter = null, int limit = 10, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var queryEmbedding = await _embeddingProvider.GenerateEmbeddingsAsync([query], ct);
        if (queryEmbedding.Length == 0)
            return EmptyResult("semantic", sw);

        // Semantic search requires PostgreSQL with pgvector
        // For InMemory (tests), return empty — semantic search is tested via integration tests
        if (!_context.Database.IsNpgsql())
            return EmptyResult("semantic", sw);

        var vectorStr = "[" + string.Join(",", queryEmbedding[0]) + "]";

        // Use raw SQL for pgvector cosine distance operator
        var results = await _context.Database
            .SqlQuery<SemanticSearchRow>($@"
                SELECT ce.""EnrichmentId"", e.""Content"", e.""FilePath"", e.""StartLine"", e.""EndLine"",
                       e.""Language"", e.""RepositoryId"", r.""Name"" AS ""RepositoryName"",
                       1.0 - (ce.""EmbeddingVector"" <=> {vectorStr}::vector) AS ""Score""
                FROM ""ContentEmbeddings"" ce
                JOIN ""Enrichments"" e ON ce.""EnrichmentId"" = e.""Id""
                JOIN ""Repositories"" r ON e.""RepositoryId"" = r.""Id""
                ORDER BY ce.""EmbeddingVector"" <=> {vectorStr}::vector
                LIMIT {limit}")
            .ToListAsync(ct);

        return new SearchResultsDto
        {
            Results = results.Select(r => new SearchResultItem
            {
                EnrichmentId = r.EnrichmentId,
                Content = r.Content,
                Score = r.Score,
                FilePath = r.FilePath,
                StartLine = r.StartLine,
                EndLine = r.EndLine,
                Language = r.Language,
                RepositoryId = r.RepositoryId,
                RepositoryName = r.RepositoryName
            }).ToList(),
            TotalCount = results.Count,
            SearchMode = "semantic",
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    // Row type for raw SQL semantic search query
    internal class SemanticSearchRow
    {
        public Guid EnrichmentId { get; set; }
        public string Content { get; set; } = "";
        public string? FilePath { get; set; }
        public int? StartLine { get; set; }
        public int? EndLine { get; set; }
        public string? Language { get; set; }
        public Guid RepositoryId { get; set; }
        public string? RepositoryName { get; set; }
        public double Score { get; set; }
    }

    public async Task<SearchResultsDto> KeywordSearchAsync(string keywords, SearchFilter? filter = null, int limit = 10, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var enrichmentQuery = _context.Enrichments
            .Include(e => e.Repository)
            .AsQueryable();

        enrichmentQuery = ApplyEnrichmentFilters(enrichmentQuery, filter);

        // Use EF.Functions.ToTsQuery for PostgreSQL full-text search
        // For InMemory provider, fall back to LIKE
        List<SearchResultItem> results;
        if (_context.Database.IsNpgsql())
        {
            var tsQuery = string.Join(" & ", keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            results = await enrichmentQuery
                .Where(e => e.SearchVector!.Matches(EF.Functions.ToTsQuery("english", tsQuery)))
                .OrderByDescending(e => e.SearchVector!.Rank(EF.Functions.ToTsQuery("english", tsQuery)))
                .Take(limit)
                .Select(e => new SearchResultItem
                {
                    EnrichmentId = e.Id,
                    Content = e.Content,
                    Score = e.SearchVector!.Rank(EF.Functions.ToTsQuery("english", tsQuery)),
                    FilePath = e.FilePath,
                    StartLine = e.StartLine,
                    EndLine = e.EndLine,
                    Language = e.Language,
                    RepositoryId = e.RepositoryId,
                    RepositoryName = e.Repository.Name
                })
                .ToListAsync(ct);
        }
        else
        {
            // InMemory fallback: simple LIKE matching
            var terms = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var term in terms)
            {
                enrichmentQuery = enrichmentQuery.Where(e => e.Content.Contains(term));
            }

            results = await enrichmentQuery
                .Take(limit)
                .Select(e => new SearchResultItem
                {
                    EnrichmentId = e.Id,
                    Content = e.Content,
                    Score = 1.0,
                    FilePath = e.FilePath,
                    StartLine = e.StartLine,
                    EndLine = e.EndLine,
                    Language = e.Language,
                    RepositoryId = e.RepositoryId,
                    RepositoryName = e.Repository.Name
                })
                .ToListAsync(ct);
        }

        return new SearchResultsDto
        {
            Results = results,
            TotalCount = results.Count,
            SearchMode = "keyword",
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    public async Task<SearchResultsDto> HybridSearchAsync(string query, SearchFilter? filter = null, int limit = 10, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Run semantic and keyword searches concurrently
        var semanticTask = SemanticSearchAsync(query, filter, limit * 2, ct);
        var keywordTask = KeywordSearchAsync(query, filter, limit * 2, ct);

        await Task.WhenAll(semanticTask, keywordTask);

        var semanticResults = semanticTask.Result;
        var keywordResults = keywordTask.Result;

        // Convert to ranked result sets for RRF
        var inputs = new List<RankedResultSet>();

        if (semanticResults.Results.Count > 0)
        {
            inputs.Add(new RankedResultSet
            {
                Source = "semantic",
                Results = semanticResults.Results.Select(r => new FusedResult
                {
                    EnrichmentId = r.EnrichmentId,
                    Content = r.Content,
                    FilePath = r.FilePath,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    Language = r.Language,
                    RepositoryId = r.RepositoryId,
                    RepositoryName = r.RepositoryName,
                    OriginalScore = r.Score
                }).ToList()
            });
        }

        if (keywordResults.Results.Count > 0)
        {
            inputs.Add(new RankedResultSet
            {
                Source = "bm25",
                Results = keywordResults.Results.Select(r => new FusedResult
                {
                    EnrichmentId = r.EnrichmentId,
                    Content = r.Content,
                    FilePath = r.FilePath,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    Language = r.Language,
                    RepositoryId = r.RepositoryId,
                    RepositoryName = r.RepositoryName,
                    OriginalScore = r.Score
                }).ToList()
            });
        }

        var fused = _fusionService.Fuse(inputs);

        return new SearchResultsDto
        {
            Results = fused.Take(limit).Select(f => new SearchResultItem
            {
                EnrichmentId = f.EnrichmentId,
                Content = f.Content,
                Score = f.FusedScore,
                FilePath = f.FilePath,
                StartLine = f.StartLine,
                EndLine = f.EndLine,
                Language = f.Language,
                RepositoryId = f.RepositoryId,
                RepositoryName = f.RepositoryName
            }).ToList(),
            TotalCount = fused.Count,
            SearchMode = "hybrid",
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    private static IQueryable<ContentEmbedding> ApplyEmbeddingFilters(IQueryable<ContentEmbedding> query, SearchFilter? filter)
    {
        if (filter is null) return query;

        if (filter.Languages is { Count: > 0 })
            query = query.Where(e => filter.Languages.Contains(e.Enrichment.Language!));
        if (filter.RepositoryIds is { Count: > 0 })
            query = query.Where(e => filter.RepositoryIds.Contains(e.Enrichment.RepositoryId));
        if (filter.FilePath is not null)
            query = query.Where(e => e.Enrichment.FilePath != null && e.Enrichment.FilePath.Contains(filter.FilePath));

        return query;
    }

    private static IQueryable<Enrichment> ApplyEnrichmentFilters(IQueryable<Enrichment> query, SearchFilter? filter)
    {
        if (filter is null) return query;

        if (filter.Languages is { Count: > 0 })
            query = query.Where(e => filter.Languages.Contains(e.Language!));
        if (filter.RepositoryIds is { Count: > 0 })
            query = query.Where(e => filter.RepositoryIds.Contains(e.RepositoryId));
        if (filter.FilePath is not null)
            query = query.Where(e => e.FilePath != null && e.FilePath.Contains(filter.FilePath));
        if (filter.CreatedAfter.HasValue)
            query = query.Where(e => e.CreatedAt >= filter.CreatedAfter.Value);
        if (filter.CreatedBefore.HasValue)
            query = query.Where(e => e.CreatedAt <= filter.CreatedBefore.Value);

        return query;
    }

    private static SearchResultsDto EmptyResult(string mode, Stopwatch sw) => new()
    {
        Results = [],
        TotalCount = 0,
        SearchMode = mode,
        DurationMs = sw.ElapsedMilliseconds
    };
}
