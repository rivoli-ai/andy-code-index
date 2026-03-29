using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;

namespace Andy.CodeIndex.Tests.Unit.Controllers;

public class ChatControllerTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly ChatController _controller;
    private readonly Mock<IChatService> _chatServiceMock = new();
    private readonly Mock<IQuestionClassifier> _classifierMock = new();
    private const string TestUserId = "test-user-123";

    public ChatControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new CodeIndexDbContext(options);

        _controller = new ChatController(_chatServiceMock.Object, _classifierMock.Object, _context);

        // Set up user claims
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, TestUserId) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private ChatConversation CreateConversation(string title, bool isPinned = false, DateTime? updatedAt = null)
    {
        var conv = new ChatConversation
        {
            Id = Guid.NewGuid(),
            UserId = TestUserId,
            Title = title,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = updatedAt ?? DateTime.UtcNow,
            IsPinned = isPinned,
            PinnedAt = isPinned ? DateTimeOffset.UtcNow : null
        };
        _context.ChatConversations.Add(conv);
        _context.SaveChanges();
        return conv;
    }

    // --- PATCH endpoint tests ---

    [Fact]
    public async Task UpdateConversation_UpdatesTitle()
    {
        var conv = CreateConversation("Old Title");
        var request = new UpdateConversationRequest(Title: "New Title");

        var result = await _controller.UpdateConversation(conv.Id, request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value;
        value.Should().NotBeNull();

        // Verify in database
        var updated = await _context.ChatConversations.FindAsync(conv.Id);
        updated!.Title.Should().Be("New Title");
    }

    [Fact]
    public async Task UpdateConversation_PinsConversation()
    {
        var conv = CreateConversation("Test Conv");
        var request = new UpdateConversationRequest(IsPinned: true);

        var result = await _controller.UpdateConversation(conv.Id, request);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _context.ChatConversations.FindAsync(conv.Id);
        updated!.IsPinned.Should().BeTrue();
        updated.PinnedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateConversation_UnpinsConversation()
    {
        var conv = CreateConversation("Test Conv", isPinned: true);
        var request = new UpdateConversationRequest(IsPinned: false);

        var result = await _controller.UpdateConversation(conv.Id, request);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _context.ChatConversations.FindAsync(conv.Id);
        updated!.IsPinned.Should().BeFalse();
        updated.PinnedAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateConversation_UpdatesTitleAndPin()
    {
        var conv = CreateConversation("Old Title");
        var request = new UpdateConversationRequest(Title: "New Title", IsPinned: true);

        var result = await _controller.UpdateConversation(conv.Id, request);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _context.ChatConversations.FindAsync(conv.Id);
        updated!.Title.Should().Be("New Title");
        updated.IsPinned.Should().BeTrue();
        updated.PinnedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateConversation_ReturnsNotFound_WhenConversationDoesNotExist()
    {
        var request = new UpdateConversationRequest(Title: "New Title");

        var result = await _controller.UpdateConversation(Guid.NewGuid(), request);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateConversation_ReturnsNotFound_WhenOtherUsersConversation()
    {
        // Create conversation for a different user
        var conv = new ChatConversation
        {
            Id = Guid.NewGuid(),
            UserId = "other-user",
            Title = "Other User Conv",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.ChatConversations.Add(conv);
        await _context.SaveChangesAsync();

        var request = new UpdateConversationRequest(Title: "Hacked Title");

        var result = await _controller.UpdateConversation(conv.Id, request);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateConversation_DoesNotChangeTitle_WhenTitleIsNull()
    {
        var conv = CreateConversation("Keep This Title");
        var request = new UpdateConversationRequest(IsPinned: true);

        await _controller.UpdateConversation(conv.Id, request);

        var updated = await _context.ChatConversations.FindAsync(conv.Id);
        updated!.Title.Should().Be("Keep This Title");
    }

    // --- ListConversations with search ---

    [Fact]
    public async Task ListConversations_ReturnsPinnedFirst()
    {
        CreateConversation("Unpinned Recent", isPinned: false, updatedAt: DateTime.UtcNow);
        CreateConversation("Pinned Old", isPinned: true, updatedAt: DateTime.UtcNow.AddDays(-5));

        var result = await _controller.ListConversations();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // The pinned conversation should come first even though it's older
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var firstPinnedIdx = json.IndexOf("Pinned Old");
        var firstUnpinnedIdx = json.IndexOf("Unpinned Recent");
        firstPinnedIdx.Should().BeLessThan(firstUnpinnedIdx);
    }

    [Fact]
    public async Task ListConversations_SearchFiltersResults()
    {
        CreateConversation("Architecture Discussion");
        CreateConversation("Bug Fix Review");

        var result = await _controller.ListConversations(search: "architecture");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("Architecture Discussion");
        json.Should().NotContain("Bug Fix Review");
    }
}
