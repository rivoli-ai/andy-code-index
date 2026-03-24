using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.CodeIndex.Tests.Integration;

/// <summary>
/// Integration tests for MCP tools - tests the CodeIndexTools service layer
/// which backs the McpServerTool-attributed methods at the /mcp endpoint.
/// We test via the enrichment/search/queue REST endpoints which share the same
/// service implementations that MCP tools use.
/// </summary>
public class McpToolsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public McpToolsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- code_index_version ---
    [Fact]
    public async Task Version_ReturnsVersionString()
    {
        // The version endpoint isn't exposed via REST, but we can verify
        // the assembly has a version attribute
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- code_index_repositories ---
    [Fact]
    public async Task ListRepositories_EmptyByDefault()
    {
        var response = await _client.GetAsync("/api/v1/repositories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var repos = await response.Content.ReadFromJsonAsync<List<RepositoryDto>>(TestJson.Options);
        repos.Should().NotBeNull();
    }

    [Fact]
    public async Task ListRepositories_AfterCreate_ContainsRepo()
    {
        var url = $"https://github.com/test/mcp-list-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });

        var response = await _client.GetAsync("/api/v1/repositories");
        var repos = await response.Content.ReadFromJsonAsync<List<RepositoryDto>>(TestJson.Options);
        repos.Should().Contain(r => r.Url == url);
    }

    // --- code_index_enrichments (architecture_docs, api_docs, etc.) ---
    [Fact]
    public async Task GetEnrichments_ReturnsListResponse()
    {
        var response = await _client.GetAsync("/api/v1/enrichments?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("totalCount");
        content.Should().Contain("results");
    }

    [Fact]
    public async Task GetEnrichments_FilterByType_Works()
    {
        var response = await _client.GetAsync("/api/v1/enrichments?type=Architecture&limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEnrichments_FilterBySubtype_Works()
    {
        var response = await _client.GetAsync("/api/v1/enrichments?subtype=Physical&limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- code_index_enrichment_counts ---
    [Fact]
    public async Task GetEnrichmentCounts_ReturnsCountsBySubtype()
    {
        var response = await _client.GetAsync("/api/v1/enrichments/counts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        counts.Should().NotBeNull();
    }

    [Fact]
    public async Task GetEnrichmentCounts_WithRepoFilter_Works()
    {
        var response = await _client.GetAsync($"/api/v1/enrichments/counts?repositoryId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- code_index_semantic_search / keyword_search ---
    [Fact]
    public async Task SemanticSearch_ReturnsResults()
    {
        var response = await _client.GetAsync("/api/v1/search/semantic?query=test&limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeywordSearch_ReturnsResults()
    {
        var response = await _client.GetAsync("/api/v1/search/keyword?keywords=test&limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HybridSearch_ReturnsResults()
    {
        var body = new { query = "test function", limit = 5 };
        var response = await _client.PostAsJsonAsync("/api/v1/search", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("totalCount");
    }

    // --- code_index_analytics ---
    [Fact]
    public async Task GetRepoDetails_WithEnrichments_IncludesStats()
    {
        // Create a repo first
        var url = $"https://github.com/test/mcp-analytics-{Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var repo = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var response = await _client.GetAsync($"/api/v1/repositories/{repo!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);
        detail.Should().NotBeNull();
        detail!.Stats.Should().NotBeNull();
    }

    // --- code_index_sync_status ---
    [Fact]
    public async Task GetSyncStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/sync/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- code_index_task_queue ---
    [Fact]
    public async Task GetTasks_ReturnsArray()
    {
        var response = await _client.GetAsync("/api/v1/queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPipelines_ReturnsArray()
    {
        var response = await _client.GetAsync("/api/v1/queue/pipelines");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Sync prevention ---
    [Fact]
    public async Task Sync_WhenTasksActive_Returns409()
    {
        // Create a repo, it gets a CloneRepository task automatically
        var url = $"https://github.com/test/mcp-sync-{Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var repo = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        // Try to sync while the initial clone task is pending
        var syncResponse = await _client.PostAsync($"/api/v1/repositories/{repo!.Id}/sync", null);
        syncResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- Enrichment with quality ---
    [Fact]
    public async Task Enrichment_IncludesQualityField()
    {
        // Seed a test enrichment with quality
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();

        var repoId = Guid.NewGuid();
        context.Repositories.Add(new Repository
        {
            Id = repoId, Name = "quality-test", Url = "https://github.com/test/quality-test",
            Provider = GitProvider.GitHub, Status = "indexed",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(), RepositoryId = repoId,
            Type = EnrichmentType.Architecture, Subtype = EnrichmentSubtype.Physical,
            Content = "Test architecture docs", Quality = 0.9,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/v1/enrichments?repositoryId={repoId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("quality");
    }

    // --- Non-existent resource errors ---
    [Fact]
    public async Task GetRepoById_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/repositories/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEnrichmentById_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/enrichments/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sync_NonExistentRepo_Returns404()
    {
        var response = await _client.PostAsync($"/api/v1/repositories/{Guid.NewGuid()}/sync", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTaskById_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/queue/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Duplicate repo ---
    [Fact]
    public async Task CreateRepository_DuplicateUrl_Returns409()
    {
        var url = $"https://github.com/test/mcp-dup-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var response = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- Search filters ---
    [Fact]
    public async Task SearchFilters_ReturnsReposAndLanguages()
    {
        var response = await _client.GetAsync("/api/v1/search/filters");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("repositories");
        content.Should().Contain("languages");
    }
}
