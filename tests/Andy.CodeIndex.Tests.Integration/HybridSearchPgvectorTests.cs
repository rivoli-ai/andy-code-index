using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Andy.CodeIndex.Tests.Integration;

/// <summary>
/// End-to-end search correctness against a real PostgreSQL + pgvector instance
/// (epic #244). EF InMemory cannot run the pgvector cosine operator or the
/// tsvector full-text path, so the previous suite never executed the semantic
/// arm, RRF fusion, or filter application — ordering was asserted nowhere.
/// These tests seed deterministic vectors into pgvector/pgvector:pg16 and assert:
/// (1) semantic ranking order, (2) the language filter now constrains the
/// semantic arm (story #251), (3) the hardened FTS query does not throw on
/// tsquery syntax characters (story #252), and (4) hybrid fusion produces a
/// descending-scored, non-empty result set.
///
/// The container is shared across all tests via a collection fixture (one
/// container, seeded once, read-only tests). The collection sets
/// DisableParallelization so this Docker-backed suite runs in isolation rather
/// than racing the ~98 in-memory WebApplicationFactory tests for resources.
/// Requires Docker.
/// </summary>
[Collection("Pgvector")]
public sealed class HybridSearchPgvectorTests
{
    private readonly PgvectorFixture _fx;

    public HybridSearchPgvectorTests(PgvectorFixture fx) => _fx = fx;

    [Fact]
    public async Task SemanticSearch_OrdersByCosineSimilarityDescending()
    {
        var result = await _fx.Search.SemanticSearchAsync("anything", filter: null, limit: 10);

        result.Results.Should().HaveCount(4);
        result.Results[0].EnrichmentId.Should().Be(PgvectorFixture.IdA, "A == e0 is identical to the query vector");
        result.Results.Select(r => r.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SemanticSearch_LanguageFilter_ConstrainsTheSemanticArm()
    {
        // Story #251 regression: before wiring the filter into the raw SQL, the
        // semantic arm ignored it and leaked csharp/javascript results.
        var filter = new SearchFilter { Languages = ["python"] };

        var result = await _fx.Search.SemanticSearchAsync("anything", filter, limit: 10);

        result.Results.Should().OnlyContain(r => r.Language == "python");
        result.Results.Select(r => r.EnrichmentId).Should().Equal(PgvectorFixture.IdA, PgvectorFixture.IdC);
    }

    [Fact]
    public async Task KeywordSearch_DoesNotThrowOnTsQuerySyntaxCharacters()
    {
        // Story #252: to_tsquery threw on ':', '(', '!', '&'. websearch_to_tsquery
        // tolerates them.
        var act = async () => await _fx.Search.KeywordSearchAsync("service: (python)! & <>", filter: null, limit: 10);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HybridSearch_FusesBothArms_OrderedDescending()
    {
        var result = await _fx.Search.HybridSearchAsync("service", filter: null, limit: 10);

        result.SearchMode.Should().Be("hybrid");
        result.Results.Should().NotBeEmpty();
        result.Results.Select(r => r.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task HybridSearch_RepositoryFilter_AppliesToBothArms()
    {
        var filter = new SearchFilter { RepositoryIds = [Guid.Parse("99999999-9999-9999-9999-999999999999")] };

        var result = await _fx.Search.HybridSearchAsync("service", filter, limit: 10);

        result.Results.Should().BeEmpty("no enrichment belongs to the filtered repository");
    }
}

/// <summary>
/// Binds <see cref="PgvectorFixture"/> to the "Pgvector" collection and disables
/// parallelization so the Docker-backed tests do not contend with the rest of
/// the assembly.
/// </summary>
[CollectionDefinition("Pgvector", DisableParallelization = true)]
public sealed class PgvectorCollection : ICollectionFixture<PgvectorFixture>;

/// <summary>
/// Shared pgvector container + seeded data for <see cref="HybridSearchPgvectorTests"/>.
/// Vectors are unit-norm so cosine similarity (= Score, since the query embedding
/// is e0) is the leading component of each: A=1.0, B=0.8, C=0.6, D=0.0.
/// </summary>
public sealed class PgvectorFixture : IAsyncLifetime
{
    private const int Dim = 1536;

    public static readonly Guid RepoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid IdA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid IdB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    public static readonly Guid IdC = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    public static readonly Guid IdD = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
    private CodeIndexDbContext _context = null!;

    public SearchService Search { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseNpgsql(_container.GetConnectionString(), o => o.UseVector())
            .Options;

        _context = new CodeIndexDbContext(options);
        await _context.Database.MigrateAsync();
        await SeedAsync();

        var provider = new StubEmbeddingProvider(Unit(0));
        Search = new SearchService(_context, provider, new RankFusionService(), NullLogger<SearchService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        _context.Repositories.Add(new Repository
        {
            Id = RepoId,
            Name = "seed-repo",
            Url = "https://example.test/seed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        // (id, language, vector, content). All contents share "service" so the
        // keyword arm returns every row.
        var seeds = new[]
        {
            (IdA, "python", Unit(0), "alpha service python module"),
            (IdB, "csharp", Blend(0, 0.8f, 1, 0.6f), "beta service csharp module"),
            (IdC, "python", Blend(0, 0.6f, 1, 0.8f), "gamma service python module"),
            (IdD, "javascript", Unit(1), "delta service javascript module"),
        };

        foreach (var (id, lang, vec, content) in seeds)
        {
            _context.Enrichments.Add(new Enrichment
            {
                Id = id,
                RepositoryId = RepoId,
                Type = EnrichmentType.Development,
                Subtype = EnrichmentSubtype.Snippet,
                Content = content,
                FilePath = $"src/{lang}/file.txt",
                Language = lang,
                CreatedAt = DateTime.UtcNow,
            });
            _context.ContentEmbeddings.Add(new ContentEmbedding
            {
                Id = Guid.NewGuid(),
                EnrichmentId = id,
                EmbeddingVector = new Vector(vec),
                IndexType = IndexType.Code,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync();
    }

    private static float[] Unit(int index)
    {
        var v = new float[Dim];
        v[index] = 1f;
        return v;
    }

    private static float[] Blend(int i0, float w0, int i1, float w1)
    {
        var v = new float[Dim];
        v[i0] = w0;
        v[i1] = w1;
        return v;
    }

    private sealed class StubEmbeddingProvider(float[] queryVector) : IEmbeddingProvider
    {
        public int Dimensions => Dim;
        public string ModelName => "stub";
        public bool IsAvailable => true;

        public Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
            => Task.FromResult(texts.Select(_ => queryVector).ToArray());
    }
}
