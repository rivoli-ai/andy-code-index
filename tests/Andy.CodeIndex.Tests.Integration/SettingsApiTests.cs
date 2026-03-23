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
    public async Task GetSettings_ReturnsDefaults()
    {
        var response = await _client.GetAsync("/api/v1/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("hasEmbeddingKey");
    }

    [Fact]
    public async Task UpdateSettings_StoresKey()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-test-key-12345678" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify key is stored (masked in response)
        var getResponse = await _client.GetAsync("/api/v1/settings");
        var body = await getResponse.Content.ReadAsStringAsync();
        body.Should().Contain("\"hasEmbeddingKey\":true");
    }

    [Fact]
    public async Task DeleteEmbeddingKey_RemovesKey()
    {
        // First set a key
        await _client.PutAsJsonAsync("/api/v1/settings",
            new { embeddingApiKey = "sk-to-delete" });

        // Then delete it
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

        // Full key should never appear
        body.Should().NotContain("sk-supersecretkey123456");
        // But masked version should
        body.Should().Contain("embeddingKeyMasked");
    }
}
