using System.Net;
using System.Net.Http.Json;
using Andy.CodeIndex.Application.DTOs;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class QueueAndAnalyticsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public QueueAndAnalyticsApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // --- Queue ---

    [Fact]
    public async Task GetQueue_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPipelines_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/queue/pipelines");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetQueueById_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/queue/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetQueue_AfterCreatingRepo_ContainsTasks()
    {
        var url = $"https://github.com/test/queue-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });

        var response = await _client.GetAsync("/api/v1/queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("CloneRepository");
    }

    // --- Analytics ---

    [Fact]
    public async Task GetAnalyticsSummary_WithRepo_Returns200()
    {
        var url = $"https://github.com/test/analytics-{Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var repo = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var response = await _client.GetAsync($"/api/v1/repositories/{repo!.Id}/analytics/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAnalyticsLanguages_WithRepo_Returns200()
    {
        var url = $"https://github.com/test/lang-{Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var repo = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var response = await _client.GetAsync($"/api/v1/repositories/{repo!.Id}/analytics/languages");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Indexing ---

    [Fact]
    public async Task GetSyncStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/sync/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("enabled");
    }

    [Fact]
    public async Task GetHistory_WithRepo_Returns200()
    {
        var url = $"https://github.com/test/history-{Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var repo = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var response = await _client.GetAsync($"/api/v1/repositories/{repo!.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
