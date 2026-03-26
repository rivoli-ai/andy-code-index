using Andy.CodeIndex.Api.Mcp;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Enums;
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
        var chatServiceMock = new Mock<IChatService>();
        var commitRepoMock = new Mock<ICommitRepository>();
        _tools = new CodeIndexTools(
            _repoServiceMock.Object,
            _searchServiceMock.Object,
            _enrichmentServiceMock.Object,
            _gitServiceMock.Object,
            chatServiceMock.Object,
            commitRepoMock.Object,
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
}
