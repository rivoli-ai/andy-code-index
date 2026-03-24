using System.Net;
using System.Net.Http.Json;
using Andy.CodeIndex.Application.DTOs;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class TaskApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TaskApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetQueue_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetQueue_EnumsSerializedAsStrings()
    {
        // Create a repo to trigger a task
        await _client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = "https://github.com/test/enum-test-" + Guid.NewGuid() });

        var response = await _client.GetAsync("/api/v1/queue");
        var body = await response.Content.ReadAsStringAsync();

        // Operation and Status should be strings, not integers
        body.Should().NotContainAny("\"operation\":0", "\"operation\":1", "\"operation\":2");
        body.Should().NotContainAny("\"status\":0", "\"status\":1", "\"status\":2");

        if (body.Contains("operation"))
        {
            body.Should().ContainAny("CloneRepository", "SyncRepository", "ScanCommit",
                "ExtractSnippets", "CreateBM25Index", "CreateCodeEmbeddings");
        }
    }

    [Fact]
    public async Task RepoList_IncludesStats()
    {
        await _client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = "https://github.com/test/stats-test-" + Guid.NewGuid() });

        var response = await _client.GetAsync("/api/v1/repositories");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("enrichmentCount");
        body.Should().Contain("embeddingCount");
        body.Should().Contain("hasEmbeddings");
    }
}
