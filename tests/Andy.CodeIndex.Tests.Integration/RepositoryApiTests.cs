using System.Net;
using System.Net.Http.Json;
using Andy.CodeIndex.Application.DTOs;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class RepositoryApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RepositoryApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_Returns200()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SwaggerEndpoint_Returns200()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Andy.CodeIndex API");
    }

    [Fact]
    public async Task CreateRepository_ValidUrl_Returns201()
    {
        var request = new CreateRepositoryRequest { Url = "https://github.com/rivoli-ai/create-" + Guid.NewGuid() };
        var response = await _client.PostAsJsonAsync("/api/v1/repositories", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var repo = await response.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);
        repo.Should().NotBeNull();
        repo!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task CreateAndGet_ReturnsCreatedRepo()
    {
        var url = "https://github.com/rivoli-ai/gettest-" + Guid.NewGuid();
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var created = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var getResponse = await _client.GetAsync($"/api/v1/repositories/{created!.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRepository_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/repositories/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateAndDelete_Returns204ThenGone()
    {
        var url = "https://github.com/rivoli-ai/deltest-" + Guid.NewGuid();
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var created = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var deleteResponse = await _client.DeleteAsync($"/api/v1/repositories/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/v1/repositories/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRepository_NonExistentId_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/v1/repositories/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateDuplicate_Returns409()
    {
        var url = "https://github.com/rivoli-ai/duptest-" + Guid.NewGuid();
        var first = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateAndSync_Returns202()
    {
        var url = "https://github.com/rivoli-ai/synctest-" + Guid.NewGuid();
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories", new CreateRepositoryRequest { Url = url });
        var created = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options);

        var syncResponse = await _client.PostAsync($"/api/v1/repositories/{created!.Id}/sync", null);
        syncResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task SyncRepository_NonExistentId_Returns404()
    {
        var response = await _client.PostAsync($"/api/v1/repositories/{Guid.NewGuid()}/sync", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnrichmentGetById_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/enrichments/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
