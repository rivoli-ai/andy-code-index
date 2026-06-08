using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Data.Interceptors;
using Andy.CodeIndex.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using Xunit;

namespace Andy.CodeIndex.Tests.Unit.Data;

/// <summary>
/// End-to-end tests for the SQLite backend: BLOB-stored embeddings ranked by
/// in-process cosine similarity, and FTS5 BM25 keyword search kept in sync by
/// <see cref="EnrichmentFtsInterceptor"/>. Uses a real (in-memory) SQLite
/// connection so the FTS5 virtual table and value converters are exercised.
/// </summary>
public class SqliteSearchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CodeIndexDbContext _context;
    private readonly StubEmbeddingProvider _embeddings = new();
    private readonly SearchService _search;
    private readonly Guid _repoId = Guid.NewGuid();

    public SqliteSearchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new EnrichmentFtsInterceptor())
            .Options;

        _context = new CodeIndexDbContext(options);
        SqliteDatabaseInitializer.Initialize(_context);

        _search = new SearchService(
            _context, _embeddings, new RankFusionService(), NullLogger<SearchService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SemanticSearch_RanksClosestEmbeddingFirst()
    {
        SeedRepo(_repoId, "repo1");
        var login = SeedEnrichment(_repoId, "user authentication and login flow", "csharp", new[] { 1f, 0f, 0f });
        SeedEnrichment(_repoId, "database migration scripts", "csharp", new[] { 0f, 1f, 0f });
        SeedEnrichment(_repoId, "frontend rendering pipeline", "typescript", new[] { 0f, 0f, 1f });
        await _context.SaveChangesAsync();

        _embeddings.Query = new[] { 0.9f, 0.1f, 0f }; // closest to the login embedding

        var results = await _search.SemanticSearchAsync("anything", limit: 3);

        Assert.NotEmpty(results.Results);
        Assert.Equal(login, results.Results[0].EnrichmentId);
        Assert.True(results.Results[0].Score > results.Results[^1].Score);
    }

    [Fact]
    public async Task SemanticSearch_RespectsLanguageFilter()
    {
        SeedRepo(_repoId, "repo1");
        SeedEnrichment(_repoId, "csharp content", "csharp", new[] { 1f, 0f, 0f });
        var ts = SeedEnrichment(_repoId, "typescript content", "typescript", new[] { 1f, 0f, 0f });
        await _context.SaveChangesAsync();

        _embeddings.Query = new[] { 1f, 0f, 0f };

        var results = await _search.SemanticSearchAsync(
            "anything", new SearchFilter { Languages = new() { "typescript" } }, limit: 10);

        Assert.All(results.Results, r => Assert.Equal("typescript", r.Language));
        Assert.Contains(results.Results, r => r.EnrichmentId == ts);
    }

    [Fact]
    public async Task KeywordSearch_ReturnsBm25RankedResults()
    {
        SeedRepo(_repoId, "repo1");
        var dense = SeedEnrichment(_repoId, "database database database connection pool", "csharp");
        var sparse = SeedEnrichment(_repoId, "database migration scripts for the service", "csharp");
        SeedEnrichment(_repoId, "unrelated rendering pipeline", "csharp");
        await _context.SaveChangesAsync();

        var results = await _search.KeywordSearchAsync("database", limit: 10);

        var ids = results.Results.Select(r => r.EnrichmentId).ToList();
        Assert.Contains(dense, ids);
        Assert.Contains(sparse, ids);
        // More frequent term occurrences in a shorter document rank higher under BM25.
        Assert.True(ids.IndexOf(dense) < ids.IndexOf(sparse));
    }

    [Fact]
    public async Task KeywordSearch_OnlyMatchesDocumentsContainingTerm()
    {
        SeedRepo(_repoId, "repo1");
        var match = SeedEnrichment(_repoId, "authentication middleware setup", "csharp");
        SeedEnrichment(_repoId, "database migration scripts", "csharp");
        await _context.SaveChangesAsync();

        var results = await _search.KeywordSearchAsync("authentication", limit: 10);

        Assert.Single(results.Results);
        Assert.Equal(match, results.Results[0].EnrichmentId);
    }

    [Fact]
    public async Task HybridSearch_ReturnsMatchingEnrichment()
    {
        SeedRepo(_repoId, "repo1");
        var target = SeedEnrichment(_repoId, "authentication and login flow", "csharp", new[] { 1f, 0f, 0f });
        SeedEnrichment(_repoId, "frontend rendering pipeline", "typescript", new[] { 0f, 0f, 1f });
        await _context.SaveChangesAsync();

        _embeddings.Query = new[] { 1f, 0f, 0f };

        var results = await _search.HybridSearchAsync("authentication", limit: 10);

        Assert.NotEmpty(results.Results);
        Assert.Contains(results.Results, r => r.EnrichmentId == target);
    }

    private void SeedRepo(Guid id, string name)
    {
        _context.Repositories.Add(new Repository
        {
            Id = id,
            Name = name,
            Url = $"https://example.test/{name}",
            Status = "indexed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private Guid SeedEnrichment(Guid repoId, string content, string language, float[]? embedding = null)
    {
        var id = Guid.NewGuid();
        var enrichment = new Enrichment
        {
            Id = id,
            RepositoryId = repoId,
            Type = EnrichmentType.Development,
            Subtype = EnrichmentSubtype.Chunk,
            Content = content,
            Language = language,
            FilePath = $"src/{id:N}.cs",
            CreatedAt = DateTime.UtcNow
        };
        _context.Enrichments.Add(enrichment);

        if (embedding is not null)
        {
            _context.ContentEmbeddings.Add(new ContentEmbedding
            {
                Id = Guid.NewGuid(),
                EnrichmentId = id,
                EmbeddingVector = new Vector(embedding),
                IndexType = IndexType.Code,
                CreatedAt = DateTime.UtcNow
            });
        }

        return id;
    }

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public float[] Query { get; set; } = { 1f, 0f, 0f };
        public int Dimensions => Query.Length;
        public string ModelName => "stub";
        public bool IsAvailable => true;

        public Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
            => Task.FromResult(texts.Select(_ => Query).ToArray());
    }
}
