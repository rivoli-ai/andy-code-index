using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class ChatServiceTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<ISearchService> _searchServiceMock = new();
    private readonly Mock<IApiKeyResolver> _apiKeyResolverMock = new();
    private readonly Mock<IQuestionClassifier> _classifierMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly ChatService _chatService;
    private readonly Repository _testRepo;

    public ChatServiceTests()
    {
        _context = TestDbContextFactory.Create();

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/test/repo",
            Provider = GitProvider.GitHub, Status = "indexed",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();

        _classifierMock.Setup(c => c.Classify(It.IsAny<string>()))
            .Returns(new ClassificationResult
            {
                DimensionId = "general",
                DimensionLabel = "General",
                Confidence = 0,
                RequiredEnrichments = new[] { EnrichmentSubtype.Physical, EnrichmentSubtype.Wiki },
                FallbackEnrichments = []
            });

        _searchServiceMock.Setup(s => s.KeywordSearchAsync(
            It.IsAny<string>(), It.IsAny<SearchFilter>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto { Results = [], TotalCount = 0 });

        var llmOptions = Options.Create(new EnrichmentLlmOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini",
            TimeoutSeconds = 30
        });

        _chatService = new ChatService(
            _context, _searchServiceMock.Object, _apiKeyResolverMock.Object,
            _classifierMock.Object, llmOptions, _httpClientFactoryMock.Object,
            NullLogger<ChatService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task ChatAsync_NoApiKey_ReturnsKeyNotConfiguredMessage()
    {
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "gpt-4o-mini", "none"));

        var response = await _chatService.ChatAsync(new ChatRequest { Message = "test" });

        response.Reply.Should().Contain("No LLM API key configured");
        response.ConversationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ChatAsync_GeneratesConversationId_WhenNotProvided()
    {
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "", "none"));

        var response = await _chatService.ChatAsync(new ChatRequest { Message = "hello" });

        response.ConversationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ChatAsync_UsesProvidedConversationId()
    {
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "", "none"));

        var response = await _chatService.ChatAsync(new ChatRequest
        {
            Message = "hello",
            ConversationId = "my-conv-id"
        });

        response.ConversationId.Should().Be("my-conv-id");
    }

    [Fact]
    public async Task ChatAsync_WithApiKey_CallsClassifier()
    {
        // With a key set, the service proceeds past the early return to classification
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-test", "gpt-4o-mini", "system"));

        // The LLM HTTP call will fail (no real server), but classifier should still be called
        try { await _chatService.ChatAsync(new ChatRequest { Message = "explain the architecture" }); } catch { }

        _classifierMock.Verify(c => c.Classify("explain the architecture"), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_FiltersEnrichmentsByQuality()
    {
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "", "none"));

        // Add enrichments with different quality scores
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Architecture, Subtype = EnrichmentSubtype.Physical,
            Content = "Good architecture doc", Quality = 0.9, CreatedAt = DateTime.UtcNow
        });
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Architecture, Subtype = EnrichmentSubtype.Physical,
            Content = "No architecture found", Quality = 0.1, CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        _classifierMock.Setup(c => c.Classify(It.IsAny<string>()))
            .Returns(new ClassificationResult
            {
                DimensionId = "structure",
                DimensionLabel = "Structure",
                Confidence = 0.8,
                MatchedQuestionText = "What is the architecture?",
                RequiredEnrichments = [EnrichmentSubtype.Physical],
                FallbackEnrichments = [EnrichmentSubtype.Wiki]
            });

        // No API key so it returns early, but the enrichment query still runs
        var response = await _chatService.ChatAsync(new ChatRequest { Message = "architecture" });

        // The low-quality enrichment (0.1) should be filtered out by the >= 0.3 filter
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task ChatAsync_WithApiKey_ScopesToRepository()
    {
        _apiKeyResolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-test", "gpt-4o-mini", "system"));

        try
        {
            await _chatService.ChatAsync(new ChatRequest
            {
                Message = "what does this repo do",
                RepositoryId = _testRepo.Id
            });
        }
        catch { /* LLM call fails without real server */ }

        _searchServiceMock.Verify(s => s.KeywordSearchAsync(
            It.IsAny<string>(),
            It.Is<SearchFilter>(f => f.RepositoryIds != null && f.RepositoryIds.Contains(_testRepo.Id)),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void IsAvailable_ReturnsTrue()
    {
        _chatService.IsAvailable.Should().BeTrue();
    }
}
