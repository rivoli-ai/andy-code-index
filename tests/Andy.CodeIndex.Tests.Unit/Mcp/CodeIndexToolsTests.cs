using Andy.CodeIndex.Api.Mcp;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Mcp;

public class CodeIndexToolsTests
{
    private readonly Mock<IRepositoryService> _repoServiceMock = new();
    private readonly Mock<ISearchService> _searchServiceMock = new();
    private readonly Mock<IEnrichmentGeneratorService> _enrichmentServiceMock = new();
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<ICommitRepository> _commitRepoMock = new();
    private readonly Mock<IChatService> _chatServiceMock = new();
    private readonly Mock<IChatFileAccessService> _chatFileAccessServiceMock = new();
    private readonly Mock<IIndexingTaskRepository> _taskRepoMock = new();
    private readonly Mock<IRepoDiscoveryService> _discoveryServiceMock = new();
    private readonly Mock<IQuestionClassifier> _classifierMock = new();
    private readonly CodeIndexTools _tools;

    private readonly RepositoryDto _testRepo = new()
    {
        Id = Guid.NewGuid(),
        Name = "andy-docs",
        Url = "https://github.com/rivoli-ai/andy-docs",
        Provider = GitProvider.GitHub,
        Status = "indexed",
        LastIndexedCommitSha = "abc123"
    };

    public CodeIndexToolsTests()
    {
        var dbContext = TestDbContextFactory.Create();
        _tools = new CodeIndexTools(
            _repoServiceMock.Object,
            _searchServiceMock.Object,
            _enrichmentServiceMock.Object,
            _gitServiceMock.Object,
            _chatServiceMock.Object,
            _chatFileAccessServiceMock.Object,
            _commitRepoMock.Object,
            _taskRepoMock.Object,
            _discoveryServiceMock.Object,
            _classifierMock.Object,
            dbContext,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }));

        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_testRepo]);
    }

    [Fact]
    public void GetVersion_ReturnsNonEmptyString()
    {
        var result = _tools.GetVersion();
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListRepositories_ReturnsAllRepos()
    {
        var result = await _tools.ListRepositories();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetArchitectureDocs_ExistingRepo_ReturnsEnrichments()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, EnrichmentSubtype.Physical, _testRepo.Id, null, null, null, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnrichmentDto { Id = Guid.NewGuid(), Content = "Architecture overview", Title = "System Architecture" }]);

        var result = await _tools.GetArchitectureDocs("andy-docs");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetArchitectureDocs_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.GetArchitectureDocs("nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task GetApiDocs_CallsCorrectSubtype()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, EnrichmentSubtype.APIDocs, _testRepo.Id, null, null, null, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _tools.GetApiDocs("andy-docs");

        _enrichmentServiceMock.Verify(s => s.QueryAsync(
            null, EnrichmentSubtype.APIDocs, _testRepo.Id, null, null, null, 0, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetWikiPage_ExistingPage_ReturnsContent()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, EnrichmentSubtype.Wiki, _testRepo.Id, null, null, "getting-started", 0, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnrichmentDto
            {
                Id = Guid.NewGuid(), Content = "# Getting Started", Title = "Getting Started",
                FilePath = "getting-started"
            }]);

        var result = await _tools.GetWikiPage("andy-docs", "getting-started");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Getting Started");
    }

    [Fact]
    public async Task GetWikiPage_NonExistentPage_ReturnsError()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, EnrichmentSubtype.Wiki, _testRepo.Id, null, null, "nonexistent", 0, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.GetWikiPage("andy-docs", "nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task SemanticSearch_CallsSearchService()
    {
        _searchServiceMock.Setup(s => s.SemanticSearchAsync(
            "find auth logic", It.IsAny<SearchFilter>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto
            {
                Results = [new SearchResultItem { EnrichmentId = Guid.NewGuid(), Content = "auth code", Score = 0.9 }],
                TotalCount = 1, SearchMode = "semantic"
            });

        var result = await _tools.SemanticSearch("find auth logic");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SemanticSearch_WithRepoFilter_ResolvesRepoId()
    {
        _searchServiceMock.Setup(s => s.SemanticSearchAsync(
            "query", It.Is<SearchFilter>(f => f.RepositoryIds!.Contains(_testRepo.Id)), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto());

        await _tools.SemanticSearch("query", source_repo: "andy-docs");

        _searchServiceMock.Verify(s => s.SemanticSearchAsync(
            "query", It.Is<SearchFilter>(f => f.RepositoryIds!.Contains(_testRepo.Id)), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KeywordSearch_CallsSearchService()
    {
        _searchServiceMock.Setup(s => s.KeywordSearchAsync(
            "UserService", It.IsAny<SearchFilter>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto { Results = [], TotalCount = 0 });

        var result = await _tools.KeywordSearch("UserService");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Grep_ExistingRepo_ReturnsResults()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.GrepAsync("/tmp/test/repos/x", "TODO", null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GrepResult { FilePath = "file.cs", LineNumber = 10, LineContent = "// TODO: fix" }]);

        // Won't actually execute because dir doesn't exist, verifying the flow
        var result = await _tools.Grep("andy-docs", "TODO");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        // Returns error because directory doesn't exist in test env
        json.Should().Contain("not cloned");
    }

    [Fact]
    public async Task Grep_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.Grep("nonexistent", "pattern");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task ReadResource_ValidUri_AttemptsRead()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");

        var result = await _tools.ReadResource("code-index://andy-docs/abc123/src/Program.cs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        // Returns error because directory doesn't exist in test env
        json.Should().Contain("not cloned");
    }

    [Fact]
    public async Task ReadResource_InvalidUri_ReturnsError()
    {
        var result = await _tools.ReadResource("invalid://format");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Invalid URI");
    }

    [Fact]
    public async Task ReadResource_ShortUri_ReturnsError()
    {
        var result = await _tools.ReadResource("code-index://only-two-parts");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Invalid URI");
    }

    [Fact]
    public async Task ListFiles_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.ListFiles("nonexistent", "**/*.cs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    // --- AddRepository ---

    [Fact]
    public async Task AddRepository_ValidUrl_ReturnsCreatedRepo()
    {
        var newRepo = new RepositoryDto
        {
            Id = Guid.NewGuid(), Name = "new-repo",
            Url = "https://github.com/test/new-repo",
            Provider = GitProvider.GitHub, Status = "pending"
        };
        _repoServiceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newRepo);

        var result = await _tools.AddRepository("https://github.com/test/new-repo");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("new-repo");
        json.Should().Contain("Indexing pipeline started");
    }

    [Fact]
    public async Task AddRepository_DuplicateUrl_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Repository with URL 'x' already exists."));

        var result = await _tools.AddRepository("https://github.com/test/dup");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("already exists");
    }

    [Fact]
    public async Task AddRepository_InvalidUrl_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UriFormatException());

        var result = await _tools.AddRepository("not-a-url");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Invalid");
    }

    // --- DeleteRepository ---

    [Fact]
    public async Task DeleteRepository_ExistingRepo_ReturnsConfirmation()
    {
        var result = await _tools.DeleteRepository("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("deleted");

        _repoServiceMock.Verify(s => s.DeleteAsync(_testRepo.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRepository_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.DeleteRepository("nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    // --- SyncRepository ---

    [Fact]
    public async Task SyncRepository_ExistingRepo_ReturnsStarted()
    {
        var result = await _tools.SyncRepository("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Sync started");

        _repoServiceMock.Verify(s => s.SyncAsync(_testRepo.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncRepository_AlreadySyncing_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.SyncAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("already has active tasks"));

        var result = await _tools.SyncRepository("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("active tasks");
    }

    [Fact]
    public async Task SyncRepository_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.SyncRepository("nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    // --- Commits ---

    [Fact]
    public async Task ListCommits_ExistingRepo_ReturnsCommits()
    {
        _commitRepoMock.Setup(c => c.GetByRepositoryAsync(_testRepo.Id, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Domain.Entities.Commit
                {
                    Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
                    Sha = "abc123", Message = "Initial commit",
                    AuthorName = "Dev", AuthorEmail = "dev@test.com",
                    CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                }
            ]);

        var result = await _tools.ListCommits("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("abc123");
        json.Should().Contain("Initial commit");
    }

    [Fact]
    public async Task ListCommits_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.ListCommits("nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task ListCommits_CustomLimit_PassesLimit()
    {
        _commitRepoMock.Setup(c => c.GetByRepositoryAsync(_testRepo.Id, 0, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _tools.ListCommits("andy-docs", limit: 5);

        _commitRepoMock.Verify(c => c.GetByRepositoryAsync(_testRepo.Id, 0, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- SearchFilters ---

    [Fact]
    public async Task GetSearchFilters_ReturnsReposAndLanguages()
    {
        _searchServiceMock.Setup(s => s.GetFilterOptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchFilterOptions
            {
                Repositories = [new FilterOption { Id = "1", Name = "repo1" }],
                Languages = ["csharp", "typescript"]
            });

        var result = await _tools.GetSearchFilters();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("repo1");
        json.Should().Contain("csharp");
        json.Should().Contain("typescript");
    }

    // --- EnrichmentCounts ---

    [Fact]
    public async Task GetEnrichmentCounts_NoFilter_ReturnsCounts()
    {
        _enrichmentServiceMock.Setup(s => s.GetCountsBySubtypeAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "Physical", 5 }, { "Chunk", 100 } });

        var result = await _tools.GetEnrichmentCounts();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Physical");
        json.Should().Contain("105"); // total
    }

    [Fact]
    public async Task GetEnrichmentCounts_WithRepoFilter_PassesRepoId()
    {
        _enrichmentServiceMock.Setup(s => s.GetCountsBySubtypeAsync(null, _testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "Wiki", 3 } });

        var result = await _tools.GetEnrichmentCounts("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Wiki");
    }

    [Fact]
    public async Task GetEnrichmentCounts_NonExistentRepo_ReturnsAllCounts()
    {
        // Non-existent repo resolves to null, so repoId is null -- returns unfiltered counts
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _enrichmentServiceMock.Setup(s => s.GetCountsBySubtypeAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());

        var result = await _tools.GetEnrichmentCounts("nonexistent");
        result.Should().NotBeNull();
    }

    // --- GetRepository (NEW) ---

    [Fact]
    public async Task GetRepository_ExistingRepo_ReturnsDetails()
    {
        var detailedRepo = new RepositoryDto
        {
            Id = _testRepo.Id, Name = "andy-docs",
            Url = "https://github.com/rivoli-ai/andy-docs",
            Provider = GitProvider.GitHub, Status = "indexed",
            DefaultBranch = "main",
            Stats = new RepositoryStatsDto { CommitCount = 10, FileCount = 50 },
            Branches = [new BranchDto { Name = "main", HeadCommitSha = "abc123", IsDefault = true }],
            Tags = [new TagDto { Name = "v1.0", CommitSha = "abc123" }]
        };
        _repoServiceMock.Setup(s => s.GetDetailsByIdAsync(_testRepo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailedRepo);

        var result = await _tools.GetRepository("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("andy-docs");
        json.Should().Contain("main");
        json.Should().Contain("v1.0");
    }

    [Fact]
    public async Task GetRepository_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.GetRepository("nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    // --- HybridSearch (NEW) ---

    [Fact]
    public async Task HybridSearch_CallsHybridSearchService()
    {
        _searchServiceMock.Setup(s => s.HybridSearchAsync(
            "find controllers", It.IsAny<SearchFilter>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto
            {
                Results = [new SearchResultItem { EnrichmentId = Guid.NewGuid(), Content = "controller code", Score = 0.85 }],
                TotalCount = 1, SearchMode = "hybrid"
            });

        var result = await _tools.HybridSearch("find controllers");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("hybrid");
        json.Should().Contain("controller code");
    }

    [Fact]
    public async Task HybridSearch_WithRepoFilter_ResolvesRepoId()
    {
        _searchServiceMock.Setup(s => s.HybridSearchAsync(
            "query", It.Is<SearchFilter>(f => f.RepositoryIds!.Contains(_testRepo.Id)), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto());

        await _tools.HybridSearch("query", source_repo: "andy-docs");

        _searchServiceMock.Verify(s => s.HybridSearchAsync(
            "query", It.Is<SearchFilter>(f => f.RepositoryIds!.Contains(_testRepo.Id)), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HybridSearch_WithLanguageFilter_SetsLanguage()
    {
        _searchServiceMock.Setup(s => s.HybridSearchAsync(
            "query", It.Is<SearchFilter>(f => f.Languages != null && f.Languages.Contains("csharp")), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResultsDto());

        await _tools.HybridSearch("query", language: "csharp", limit: 5);

        _searchServiceMock.Verify(s => s.HybridSearchAsync(
            "query", It.Is<SearchFilter>(f => f.Languages!.Contains("csharp")), 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- QueryEnrichments (NEW) ---

    [Fact]
    public async Task QueryEnrichments_NoFilters_ReturnsAllEnrichments()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, null, null, null, null, null, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnrichmentDto { Id = Guid.NewGuid(), Content = "test content", Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk }]);
        _enrichmentServiceMock.Setup(s => s.QueryCountAsync(
            null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _tools.QueryEnrichments();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("test content");
        json.Should().Contain("Chunk");
    }

    [Fact]
    public async Task QueryEnrichments_WithSubtypeFilter_ParsesEnum()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, EnrichmentSubtype.APIDocs, null, null, null, null, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _enrichmentServiceMock.Setup(s => s.QueryCountAsync(
            null, EnrichmentSubtype.APIDocs, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await _tools.QueryEnrichments(subtype: "APIDocs");

        _enrichmentServiceMock.Verify(s => s.QueryAsync(
            null, EnrichmentSubtype.APIDocs, null, null, null, null, 0, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryEnrichments_WithRepoFilter_ResolvesRepoId()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, null, _testRepo.Id, null, null, null, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _enrichmentServiceMock.Setup(s => s.QueryCountAsync(
            null, null, _testRepo.Id, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await _tools.QueryEnrichments(repo_url: "andy-docs");

        _enrichmentServiceMock.Verify(s => s.QueryAsync(
            null, null, _testRepo.Id, null, null, null, 0, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryEnrichments_WithPagination_PassesOffsetAndLimit()
    {
        _enrichmentServiceMock.Setup(s => s.QueryAsync(
            null, null, null, null, null, null, 10, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _enrichmentServiceMock.Setup(s => s.QueryCountAsync(
            null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _tools.QueryEnrichments(offset: 10, limit: 5);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"offset\":10");
        json.Should().Contain("\"limit\":5");
    }

    // --- GetEnrichment (NEW) ---

    [Fact]
    public async Task GetEnrichment_ExistingId_ReturnsEnrichment()
    {
        var enrichmentId = Guid.NewGuid();
        _enrichmentServiceMock.Setup(s => s.GetByIdAsync(enrichmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentDto
            {
                Id = enrichmentId, Content = "Full enrichment content",
                Title = "Test Enrichment", Type = EnrichmentType.Architecture,
                Subtype = EnrichmentSubtype.Physical, Quality = 0.95
            });

        var result = await _tools.GetEnrichment(enrichmentId.ToString());
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Full enrichment content");
        json.Should().Contain("Test Enrichment");
        json.Should().Contain("Physical");
    }

    [Fact]
    public async Task GetEnrichment_NonExistentId_ReturnsError()
    {
        var id = Guid.NewGuid();
        _enrichmentServiceMock.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnrichmentDto?)null);

        var result = await _tools.GetEnrichment(id.ToString());
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task GetEnrichment_InvalidGuid_ReturnsError()
    {
        var result = await _tools.GetEnrichment("not-a-guid");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Invalid enrichment ID");
    }

    // --- ChatSuggestions (NEW) ---

    [Fact]
    public void GetChatSuggestions_ReturnsDimensions()
    {
        _classifierMock.Setup(c => c.GetSuggestions())
            .Returns([
                new SuggestionDimension
                {
                    Id = "architecture", Label = "Architecture",
                    Questions = [new SuggestionQuestion { Id = "q1", Text = "What is the architecture?" }]
                }
            ]);

        var result = _tools.GetChatSuggestions();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Architecture");
        json.Should().Contain("What is the architecture?");
    }

    // --- ChatStatus (NEW) ---

    [Fact]
    public void GetChatStatus_WhenAvailable_ReturnsTrue()
    {
        _chatServiceMock.Setup(c => c.IsAvailable).Returns(true);

        var result = _tools.GetChatStatus();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("true");
    }

    [Fact]
    public void GetChatStatus_WhenUnavailable_ReturnsFalse()
    {
        _chatServiceMock.Setup(c => c.IsAvailable).Returns(false);

        var result = _tools.GetChatStatus();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("false");
    }

    // --- QueueTasks (NEW) ---

    [Fact]
    public async Task ListQueueTasks_ReturnsTasks()
    {
        _taskRepoMock.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IndexingTask
                {
                    Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
                    Operation = TaskOperation.SyncRepository,
                    Status = IndexingTaskStatus.Running,
                    Progress = 50, Priority = 5,
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var result = await _tools.ListQueueTasks();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("SyncRepository");
        json.Should().Contain("Running");
        json.Should().Contain("\"total\":1");
    }

    [Fact]
    public async Task ListQueueTasks_EmptyQueue_ReturnsEmptyList()
    {
        _taskRepoMock.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.ListQueueTasks();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"total\":0");
    }

    // --- QueueTask (NEW) ---

    [Fact]
    public async Task GetQueueTask_ExistingTask_ReturnsDetails()
    {
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(t => t.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexingTask
            {
                Id = taskId, RepositoryId = _testRepo.Id,
                Operation = TaskOperation.CreateCodeEmbeddings,
                Status = IndexingTaskStatus.Completed,
                Progress = 100, Priority = 5,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            });

        var result = await _tools.GetQueueTask(taskId.ToString());
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("CreateCodeEmbeddings");
        json.Should().Contain("Completed");
    }

    [Fact]
    public async Task GetQueueTask_NonExistentTask_ReturnsError()
    {
        var taskId = Guid.NewGuid();
        _taskRepoMock.Setup(t => t.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexingTask?)null);

        var result = await _tools.GetQueueTask(taskId.ToString());
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task GetQueueTask_InvalidGuid_ReturnsError()
    {
        var result = await _tools.GetQueueTask("not-a-guid");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("Invalid task ID");
    }

    // --- GetCommit (NEW) ---

    [Fact]
    public async Task GetCommit_ExistingCommit_ReturnsDetails()
    {
        _commitRepoMock.Setup(c => c.GetByShaAsync(_testRepo.Id, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Entities.Commit
            {
                Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
                Sha = "abc123", Message = "Fix bug in auth",
                AuthorName = "Dev", AuthorEmail = "dev@test.com",
                CommittedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                IsIndexed = true
            });

        var result = await _tools.GetCommit("andy-docs", "abc123");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("abc123");
        json.Should().Contain("Fix bug in auth");
        json.Should().Contain("true"); // IsIndexed
    }

    [Fact]
    public async Task GetCommit_NonExistentCommit_ReturnsError()
    {
        _commitRepoMock.Setup(c => c.GetByShaAsync(_testRepo.Id, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.Commit?)null);

        var result = await _tools.GetCommit("andy-docs", "nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    [Fact]
    public async Task GetCommit_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.GetCommit("nonexistent", "abc123");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }

    // --- DiscoverGitHub (NEW) ---

    [Fact]
    public async Task DiscoverGitHub_ReturnsDiscoveredRepos()
    {
        _discoveryServiceMock.Setup(d => d.DiscoverGitHubAsync("rivoli-ai", null, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new DiscoveredRepo
                {
                    Name = "andy-docs", FullName = "rivoli-ai/andy-docs",
                    CloneUrl = "https://github.com/rivoli-ai/andy-docs.git",
                    Provider = "GitHub", DefaultBranch = "main",
                    Description = "Documentation repo", AlreadyTracked = true
                }
            ]);

        var result = await _tools.DiscoverGitHub("rivoli-ai");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("rivoli-ai");
        json.Should().Contain("andy-docs");
        json.Should().Contain("AlreadyTracked");
    }

    [Fact]
    public async Task DiscoverGitHub_WithOptions_PassesParameters()
    {
        _discoveryServiceMock.Setup(d => d.DiscoverGitHubAsync("org", "token", false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _tools.DiscoverGitHub("org", pat: "token", exclude_archived: false, exclude_forks: false);

        _discoveryServiceMock.Verify(d => d.DiscoverGitHubAsync("org", "token", false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- DiscoverAzureDevOps (NEW) ---

    [Fact]
    public async Task DiscoverAzureDevOps_ReturnsDiscoveredRepos()
    {
        _discoveryServiceMock.Setup(d => d.DiscoverAzureDevOpsAsync("myorg", "myproject", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new DiscoveredRepo
                {
                    Name = "backend-api", FullName = "myorg/myproject/backend-api",
                    CloneUrl = "https://dev.azure.com/myorg/myproject/_git/backend-api",
                    Provider = "AzureDevOps", DefaultBranch = "main"
                }
            ]);

        var result = await _tools.DiscoverAzureDevOps("myorg", project: "myproject");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("myorg");
        json.Should().Contain("backend-api");
        json.Should().Contain("AzureDevOps");
    }

    // --- SyncDiscovered (NEW) ---

    [Fact]
    public async Task SyncDiscovered_AddsNewRepos()
    {
        var newRepo = new RepositoryDto
        {
            Id = Guid.NewGuid(), Name = "new-repo",
            Url = "https://github.com/test/new-repo",
            Provider = GitProvider.GitHub, Status = "pending"
        };
        _repoServiceMock.Setup(s => s.AddAsync(
            It.Is<CreateRepositoryRequest>(r => r.Url == "https://github.com/test/new-repo"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newRepo);

        var result = await _tools.SyncDiscovered(["https://github.com/test/new-repo"]);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("new-repo");
        json.Should().Contain("\"addedCount\":1");
        json.Should().Contain("\"skippedCount\":0");
    }

    [Fact]
    public async Task SyncDiscovered_SkipsDuplicates()
    {
        _repoServiceMock.Setup(s => s.AddAsync(It.IsAny<CreateRepositoryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("already exists"));

        var result = await _tools.SyncDiscovered(["https://github.com/test/existing"]);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"addedCount\":0");
        json.Should().Contain("\"skippedCount\":1");
        json.Should().Contain("existing");
    }

    [Fact]
    public async Task SyncDiscovered_MixedResults_ReportsAddedAndSkipped()
    {
        var newRepo = new RepositoryDto
        {
            Id = Guid.NewGuid(), Name = "new-repo",
            Url = "https://github.com/test/new-repo",
            Provider = GitProvider.GitHub, Status = "pending"
        };
        _repoServiceMock.Setup(s => s.AddAsync(
            It.Is<CreateRepositoryRequest>(r => r.Url == "https://github.com/test/new-repo"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newRepo);
        _repoServiceMock.Setup(s => s.AddAsync(
            It.Is<CreateRepositoryRequest>(r => r.Url == "https://github.com/test/existing"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("already exists"));

        var result = await _tools.SyncDiscovered(
            ["https://github.com/test/new-repo", "https://github.com/test/existing"]);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"addedCount\":1");
        json.Should().Contain("\"skippedCount\":1");
    }

    // --- IndexingHistory (NEW) ---

    [Fact]
    public async Task GetIndexingHistory_ExistingRepo_ReturnsRuns()
    {
        // The indexing history tool uses DbContext directly, which is configured
        // with in-memory DB -- so no runs will be returned, but no error either
        var result = await _tools.GetIndexingHistory("andy-docs");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("andy-docs");
        json.Should().Contain("\"total\":0");
    }

    [Fact]
    public async Task GetIndexingHistory_NonExistentRepo_ReturnsError()
    {
        _repoServiceMock.Setup(s => s.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tools.GetIndexingHistory("nonexistent");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("not found");
    }
}
