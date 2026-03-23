using System.Net;
using System.Net.Http.Json;
using Andy.CodeIndex.Application.DTOs;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class DiscoveryApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DiscoveryApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SyncDiscovered_AddsNewRepos()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/discover/sync", new
        {
            repositoryUrls = new[]
            {
                "https://github.com/test/sync-discover-" + Guid.NewGuid()
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("added");
    }

    [Fact]
    public async Task SyncDiscovered_SkipsDuplicates()
    {
        var url = "https://github.com/test/dup-discover-" + Guid.NewGuid();

        // Add first
        await _client.PostAsJsonAsync("/api/v1/discover/sync", new { repositoryUrls = new[] { url } });

        // Try again — should skip
        var response = await _client.PostAsJsonAsync("/api/v1/discover/sync", new { repositoryUrls = new[] { url } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("skipped");
    }

    [Fact]
    public async Task MultiRepoSearch_CrossRepo_Returns200()
    {
        // Add two repos
        await _client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = "https://github.com/test/multi-a-" + Guid.NewGuid() });
        await _client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = "https://github.com/test/multi-b-" + Guid.NewGuid() });

        // Search across all repos (no repo filter)
        var response = await _client.GetAsync("/api/v1/search/keyword?keywords=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
