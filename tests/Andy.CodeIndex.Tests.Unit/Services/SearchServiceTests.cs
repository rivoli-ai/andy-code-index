using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class SearchServiceTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IEmbeddingProvider> _providerMock = new();
    private readonly SearchService _service;
    private readonly Repository _testRepo;

    public SearchServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _service = new SearchService(
            _context,
            _providerMock.Object,
            new RankFusionService(),
            NullLogger<SearchService>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private void SeedEnrichments(params (string content, string? language, string? filePath)[] items)
    {
        foreach (var (content, language, filePath) in items)
        {
            _context.Enrichments.Add(new Enrichment
            {
                Id = Guid.NewGuid(),
                RepositoryId = _testRepo.Id,
                Type = EnrichmentType.Development,
                Subtype = EnrichmentSubtype.Chunk,
                Content = content,
                Language = language,
                FilePath = filePath,
                CreatedAt = DateTime.UtcNow
            });
        }
        _context.SaveChanges();
    }

    [Fact]
    public async Task KeywordSearchAsync_MatchesContent()
    {
        SeedEnrichments(
            ("public class UserService { }", "csharp", "UserService.cs"),
            ("def calculate_total(items):", "python", "calc.py"),
            ("public class OrderService { }", "csharp", "OrderService.cs"));

        // InMemory uses LIKE fallback
        var result = await _service.KeywordSearchAsync("UserService");

        result.Results.Should().HaveCount(1);
        result.Results[0].Content.Should().Contain("UserService");
        result.SearchMode.Should().Be("keyword");
    }

    [Fact]
    public async Task KeywordSearchAsync_MultipleTerms_MatchesAll()
    {
        SeedEnrichments(
            ("public class UserService implements IService { }", "csharp", "UserService.cs"),
            ("public class OrderService { }", "csharp", "OrderService.cs"));

        var result = await _service.KeywordSearchAsync("UserService IService");

        result.Results.Should().HaveCount(1);
        result.Results[0].Content.Should().Contain("UserService");
    }

    [Fact]
    public async Task KeywordSearchAsync_NoMatches_ReturnsEmpty()
    {
        SeedEnrichments(("some code here", "csharp", "file.cs"));

        var result = await _service.KeywordSearchAsync("nonexistent");

        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task KeywordSearchAsync_WithLanguageFilter_NarrowsResults()
    {
        SeedEnrichments(
            ("class Service { }", "csharp", "s.cs"),
            ("class Service:", "python", "s.py"));

        var result = await _service.KeywordSearchAsync("Service",
            filter: new SearchFilter { Languages = ["csharp"] });

        result.Results.Should().HaveCount(1);
        result.Results[0].Language.Should().Be("csharp");
    }

    [Fact]
    public async Task KeywordSearchAsync_RespectsLimit()
    {
        SeedEnrichments(
            ("match one", "csharp", "a.cs"),
            ("match two", "csharp", "b.cs"),
            ("match three", "csharp", "c.cs"));

        var result = await _service.KeywordSearchAsync("match", limit: 2);

        result.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SemanticSearchAsync_InMemory_ReturnsEmpty()
    {
        // Semantic search requires PostgreSQL, returns empty on InMemory
        _providerMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new float[] { 1, 2, 3 } });

        var result = await _service.SemanticSearchAsync("test query");

        result.Results.Should().BeEmpty();
        result.SearchMode.Should().Be("semantic");
    }

    [Fact]
    public async Task SemanticSearchAsync_EmptyEmbedding_ReturnsEmpty()
    {
        _providerMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<float[]>());

        var result = await _service.SemanticSearchAsync("test");

        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task HybridSearchAsync_CombinesKeywordResults()
    {
        SeedEnrichments(
            ("public class UserService { }", "csharp", "UserService.cs"),
            ("public class OrderService { }", "csharp", "OrderService.cs"));

        _providerMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new float[] { 1, 2, 3 } });

        // On InMemory: semantic returns empty, keyword returns matches
        var result = await _service.HybridSearchAsync("UserService");

        result.SearchMode.Should().Be("hybrid");
        result.Results.Should().HaveCount(1); // Only keyword results in InMemory
        result.DurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task KeywordSearchAsync_IncludesRepositoryName()
    {
        SeedEnrichments(("some code", "csharp", "file.cs"));

        var result = await _service.KeywordSearchAsync("code");

        result.Results.Should().HaveCount(1);
        result.Results[0].RepositoryName.Should().Be("test-repo");
        result.Results[0].RepositoryId.Should().Be(_testRepo.Id);
    }
}
