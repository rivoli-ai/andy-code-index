using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Produces("application/json")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IQuestionClassifier _classifier;
    private readonly CodeIndexDbContext _context;

    public ChatController(IChatService chatService, IQuestionClassifier classifier, CodeIndexDbContext context)
    {
        _chatService = chatService;
        _classifier = classifier;
        _context = context;
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? "anonymous";

    /// <summary>Chat with the indexed codebase using RAG.</summary>
    [RequirePermission("search:read")]    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct = default)
    {
        var response = await _chatService.ChatAsync(request, GetUserId(), ct);
        return Ok(response);
    }

    /// <summary>Get suggested questions organized by dimension.</summary>
    [HttpGet("suggestions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSuggestions()
    {
        return Ok(new { dimensions = _classifier.GetSuggestions() });
    }

    /// <summary>List user's conversations.</summary>
    [RequirePermission("search:read")]
    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations(
        [FromQuery] int offset = 0, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var conversations = await _context.ChatConversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(offset).Take(limit)
            .Select(c => new
            {
                c.Id, c.Title, c.RepositoryId, c.CreatedAt, c.UpdatedAt,
                messageCount = c.Messages.Count
            })
            .ToListAsync(ct);

        var total = await _context.ChatConversations.CountAsync(c => c.UserId == userId, ct);
        return Ok(new { conversations, total, offset, limit });
    }

    /// <summary>Get a conversation with messages.</summary>
    [RequirePermission("search:read")]
    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> GetConversation(Guid id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var conversation = await _context.ChatConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

        if (conversation is null) return NotFound();

        return Ok(new
        {
            conversation.Id, conversation.Title, conversation.RepositoryId,
            conversation.CreatedAt, conversation.UpdatedAt,
            messages = conversation.Messages.OrderBy(m => m.CreatedAt).Select(m => new
            {
                m.Id, m.Role, m.Content, m.SourcesJson, m.CreatedAt
            })
        });
    }

    /// <summary>Delete a conversation and all messages.</summary>
    [RequirePermission("search:read")]
    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> DeleteConversation(Guid id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var conversation = await _context.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

        if (conversation is null) return NotFound();

        _context.ChatConversations.Remove(conversation);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Rename a conversation.</summary>
    [RequirePermission("search:read")]
    [HttpPut("conversations/{id:guid}/title")]
    public async Task<IActionResult> RenameConversation(
        Guid id, [FromBody] RenameRequest request, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var conversation = await _context.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

        if (conversation is null) return NotFound();

        conversation.Title = request.Title;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return Ok(new { conversation.Id, conversation.Title });
    }

    /// <summary>Check if chat is available (LLM configured).</summary>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        return Ok(new { available = _chatService.IsAvailable });
    }
}

public record RenameRequest(string Title);
