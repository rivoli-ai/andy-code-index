using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class SettingsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SettingsApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSettings_ReturnsEmbeddingState()
    {
        var response = await _client.GetAsync("/api/v1/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("embedding");
        body.Should().Contain("source");
        body.Should().Contain("hasKey");
    }

    [Fact]
    public async Task UpdateSettings_StoresKeyAndShowsMasked()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-test-key-12345678" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync("/api/v1/settings");
        var body = await getResponse.Content.ReadAsStringAsync();
        body.Should().Contain("\"hasKey\":true");
        body.Should().Contain("\"source\":\"user\"");
        body.Should().Contain("***...5678"); // Masked: last 4 chars
    }

    [Fact]
    public async Task DeleteEmbeddingKey_RemovesKey()
    {
        await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-to-delete-1234" });

        var response = await _client.DeleteAsync("/api/v1/settings/embedding-key");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSettings_MasksKey_NeverReturnsFullKey()
    {
        await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-supersecretkey123456" });

        var response = await _client.GetAsync("/api/v1/settings");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("sk-supersecretkey123456");
        body.Should().Contain("***...3456"); // andy-docs format: ***...last4
    }

    [Fact]
    public async Task GetHistory_ReturnsAuditTrail()
    {
        // Set a key to create an audit entry
        await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-audit-test-key-9999" });

        var response = await _client.GetAsync("/api/v1/settings/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("EmbeddingApiKey");
        body.Should().Contain("set"); // action
    }

    [Fact]
    public async Task UpdateKey_AuditShowsUpdated()
    {
        // Set initial key
        await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-first-key-0000" });

        // Update key
        await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-second-key-1111" });

        var response = await _client.GetAsync("/api/v1/settings/history");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("updated"); // Second change should be "updated" not "set"
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthStatus()
    {
        var response = await _client.GetAsync("/api/v1/settings/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("llmKeyValid");
        body.Should().Contain("embeddingKeyValid");
        body.Should().Contain("lastChecked");
    }
}
