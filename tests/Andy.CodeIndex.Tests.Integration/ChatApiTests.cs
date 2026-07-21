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
    public async Task GetSuggestions_ReturnsAllOntologyDimensions()
    {
        var response = await _client.GetAsync("/api/v1/chat/suggestions");
        var result = await response.Content.ReadFromJsonAsync<SuggestionsResponse>(TestJson.Options);
        result.Should().NotBeNull();
        result!.Dimensions.Should().HaveCount(11);
        result.Dimensions.Should().Contain(d => d.Id == "operations");
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

    // --- Conversation management ---

    [Fact]
    public async Task ListConversations_Returns200WithStructure()
    {
        var response = await _client.GetAsync("/api/v1/chat/conversations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("conversations");
        body.Should().Contain("total");
    }

    [Fact]
    public async Task Chat_CreatesConversation_ThenListReturnsIt()
    {
        // Send a chat message to create a conversation
        var chatResponse = await _client.PostAsJsonAsync("/api/v1/chat",
            new { message = "What is this repo about?" });
        chatResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var chatBody = await chatResponse.Content.ReadAsStringAsync();
        chatBody.Should().Contain("conversationId");

        // List conversations
        var listResponse = await _client.GetAsync("/api/v1/chat/conversations");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().Contain("What is this repo about");
    }

    [Fact]
    public async Task GetConversation_ReturnsMessages()
    {
        // Create via chat
        var chatResponse = await _client.PostAsJsonAsync("/api/v1/chat",
            new { message = "Test message for get" });
        var chatResult = await chatResponse.Content.ReadFromJsonAsync<ChatResultDto>(TestJson.Options);

        // Get the conversation
        var response = await _client.GetAsync($"/api/v1/chat/conversations/{chatResult!.ConversationId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Test message for get");
        body.Should().Contain("messages");
    }

    [Fact]
    public async Task DeleteConversation_RemovesIt()
    {
        // Create via chat
        var chatResponse = await _client.PostAsJsonAsync("/api/v1/chat",
            new { message = "To be deleted" });
        var chatResult = await chatResponse.Content.ReadFromJsonAsync<ChatResultDto>(TestJson.Options);

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/v1/chat/conversations/{chatResult!.ConversationId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var getResponse = await _client.GetAsync($"/api/v1/chat/conversations/{chatResult.ConversationId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RenameConversation_UpdatesTitle()
    {
        var chatResponse = await _client.PostAsJsonAsync("/api/v1/chat",
            new { message = "Original title question" });
        var chatResult = await chatResponse.Content.ReadFromJsonAsync<ChatResultDto>(TestJson.Options);

        var renameResponse = await _client.PutAsJsonAsync(
            $"/api/v1/chat/conversations/{chatResult!.ConversationId}/title",
            new { title = "Renamed conversation" });
        renameResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await renameResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Renamed conversation");
    }

    [Fact]
    public async Task GetConversation_OtherUser_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/chat/conversations/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private class ChatResultDto
    {
        public string ConversationId { get; set; } = "";
        public string Reply { get; set; } = "";
    }
}
