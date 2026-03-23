using System.Net;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class AuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_AllowsAnonymous()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SwaggerEndpoint_AllowsAnonymous()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiEndpoint_WithTestAuth_Returns200()
    {
        // TestAuthHandler auto-authenticates in test factory
        var response = await _client.GetAsync("/api/v1/repositories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiEndpoint_Returns200_ForAllControllers()
    {
        (await _client.GetAsync("/api/v1/repositories")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/api/v1/enrichments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/api/v1/queue")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/api/v1/search/keyword?keywords=test")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
