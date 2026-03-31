using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Text.Json;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class InsightsHandlerTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IApiKeyResolver> _apiKeyResolverMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly InsightsHandler _handler;
    private readonly Repository _testRepo;
    private readonly Commit _testCommit;

    public InsightsHandlerTests()
    {
        _context = TestDbContextFactory.Create();

        _handler = new InsightsHandler(
            _context,
            _apiKeyResolverMock.Object,
            Options.Create(new EnrichmentLlmOptions
            {
                BaseUrl = "https://api.example.com/v1",
                TimeoutSeconds = 30
            }),
            _httpClientFactoryMock.Object,
            NullLogger<InsightsHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub,
            LastIndexedCommitSha = "abc123",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);

        _testCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "abc123",
            Message = "Initial commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(_testCommit);

        // Seed a chunk so the handler doesn't skip due to missing base enrichments
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk,
            Title = "Test chunk", Content = "test code", CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsCreateInsights()
    {
        _handler.Operation.Should().Be(TaskOperation.CreateInsights);
    }

    [Fact]
    public void GetInsightLayers_Returns11Layers()
    {
        var layers = InsightsHandler.GetInsightLayers(_testRepo, "", "");
        layers.Should().HaveCount(11);
    }

    [Fact]
    public void GetInsightLayers_AllHaveDistinctSubtypes()
    {
        var layers = InsightsHandler.GetInsightLayers(_testRepo, "", "");
        var subtypes = layers.Select(l => l.Subtype).ToList();
        subtypes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetInsightLayers_ContainsExpectedSubtypes()
    {
        var layers = InsightsHandler.GetInsightLayers(_testRepo, "", "");
        var subtypes = layers.Select(l => l.Subtype).ToHashSet();

        subtypes.Should().Contain(EnrichmentSubtype.FeatureMap);
        subtypes.Should().Contain(EnrichmentSubtype.ArchitectureAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.DesignAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.ImplementationAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.DependencyAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.TestAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.SecurityAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.DeploymentAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.OperationsAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.LocalSetupGuide);
        subtypes.Should().Contain(EnrichmentSubtype.TechStack);
    }

    [Fact]
    public void GetInsightLayers_PromptsIncludeRepoName()
    {
        var layers = InsightsHandler.GetInsightLayers(_testRepo, "", "");
        foreach (var layer in layers)
        {
            layer.Prompt.Should().Contain(_testRepo.Name,
                because: $"the {layer.Subtype} prompt should reference the repository name");
        }
    }

    [Fact]
    public void GetInsightLayers_PromptsIncludeExistingContext()
    {
        var existingContext = "=== Physical === Some architecture docs";
        var layers = InsightsHandler.GetInsightLayers(_testRepo, existingContext, "");
        foreach (var layer in layers)
        {
            layer.Prompt.Should().Contain("Some architecture docs",
                because: $"the {layer.Subtype} prompt should include existing context");
        }
    }

    [Fact]
    public async Task HandleAsync_NoLlmKey_SkipsWithoutError()
    {
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "https://api.openai.com/v1", "", ""));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _context.Enrichments
            .Count(e => e.RepositoryId == _testRepo.Id && e.Type == EnrichmentType.Insights)
            .Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_NonExistentRepo_Throws()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(), // Not in DB
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        var act = () => _handler.HandleAsync(task);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task HandleAsync_SetsCommitIdOnEnrichments()
    {
        SetupLlmMock("LLM response content for testing.");
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-key", "https://api.openai.com/v1", "test-model", "test"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichments = await _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id && e.Type == EnrichmentType.Insights)
            .ToListAsync();

        enrichments.Should().HaveCount(11);
        foreach (var enrichment in enrichments)
        {
            enrichment.CommitId.Should().Be(_testCommit.Id,
                because: "all insight enrichments should be linked to the commit");
        }
    }

    [Fact]
    public async Task HandleAsync_CreatesAllInsightSubtypes()
    {
        SetupLlmMock("Generated insight content for testing purposes.");
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-key", "https://api.openai.com/v1", "test-model", "test"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichments = await _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id && e.Type == EnrichmentType.Insights)
            .ToListAsync();

        var subtypes = enrichments.Select(e => e.Subtype).ToHashSet();
        subtypes.Should().Contain(EnrichmentSubtype.FeatureMap);
        subtypes.Should().Contain(EnrichmentSubtype.ArchitectureAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.DesignAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.ImplementationAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.DependencyAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.TestAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.SecurityAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.DeploymentAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.OperationsAnalysis);
        subtypes.Should().Contain(EnrichmentSubtype.LocalSetupGuide);
        subtypes.Should().Contain(EnrichmentSubtype.TechStack);
    }

    [Fact]
    public async Task HandleAsync_ReplacesExistingInsights()
    {
        // Add an existing insight
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Insights,
            Subtype = EnrichmentSubtype.FeatureMap,
            Title = "Old Feature Map",
            Content = "Old content",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        SetupLlmMock("New insight content replaces old.");
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-key", "https://api.openai.com/v1", "test-model", "test"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var featureMaps = await _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.FeatureMap)
            .ToListAsync();

        featureMaps.Should().HaveCount(1, because: "old insight should be replaced, not duplicated");
        featureMaps[0].Content.Should().Be("New insight content replaces old.");
    }

    [Fact]
    public async Task HandleAsync_SetsQualityScore()
    {
        SetupLlmMock(new string('x', 2500)); // Long content = high quality
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-key", "https://api.openai.com/v1", "test-model", "test"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichments = await _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id && e.Type == EnrichmentType.Insights)
            .ToListAsync();

        foreach (var enrichment in enrichments)
        {
            enrichment.Quality.Should().Be(1.0, because: "long content should have high quality score");
        }
    }

    [Fact]
    public async Task HandleAsync_UsesExistingEnrichmentsAsContext()
    {
        // Add existing enrichments that should be included in context
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Architecture,
            Subtype = EnrichmentSubtype.Physical,
            Title = "Architecture",
            Content = "This is the architecture documentation",
            CreatedAt = DateTime.UtcNow
        });
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Architecture,
            Subtype = EnrichmentSubtype.Dependencies,
            Title = "Dependencies",
            Content = "NuGet packages: Moq, FluentAssertions",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        SetupLlmMock("Response based on existing context.");
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-key", "https://api.openai.com/v1", "test-model", "test"));

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateInsights,
            CreatedAt = DateTime.UtcNow
        };

        // Should not throw -- handler should successfully use existing enrichments as context
        await _handler.HandleAsync(task);

        _context.Enrichments
            .Count(e => e.RepositoryId == _testRepo.Id && e.Type == EnrichmentType.Insights)
            .Should().Be(11);
    }

    private void SetupLlmMock(string responseContent)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = responseContent } }
            }
        });

        var mockHandler = new MockHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(mockHandler);

        _httpClientFactoryMock.Setup(f => f.CreateClient("Chat")).Returns(httpClient);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public MockHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
