using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Integration;

public class ChatApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ChatApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/chat/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("available");
    }

    [Fact]
    public async Task GetSuggestions_ReturnsDimensions()
    {
        var response = await _client.GetAsync("/api/v1/chat/suggestions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("dimensions");
        body.Should().Contain("Structure");
        body.Should().Contain("questions");
    }

    [Fact]
    public async Task GetSuggestions_Returns10Dimensions()
    {
        var response = await _client.GetAsync("/api/v1/chat/suggestions");
        var result = await response.Content.ReadFromJsonAsync<SuggestionsResponse>(TestJson.Options);
        result.Should().NotBeNull();
        result!.Dimensions.Should().HaveCount(10);
    }

    [Fact]
    public async Task Chat_WithoutLlmKey_ReturnsKeyNotConfigured()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/chat",
            new { message = "What does this repo do?" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("No LLM API key configured");
    }

    [Fact]
    public async Task Chat_ReturnsConversationId()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/chat",
            new { message = "test" });
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("conversationId");
    }

    private class SuggestionsResponse
    {
        public List<DimensionDto> Dimensions { get; set; } = [];
    }

    private class DimensionDto
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public List<QuestionDto> Questions { get; set; } = [];
    }

    private class QuestionDto
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
    }
}
