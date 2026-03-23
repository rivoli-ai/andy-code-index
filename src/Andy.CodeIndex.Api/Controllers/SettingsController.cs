using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/settings")]
[Produces("application/json")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly CodeIndexDbContext _context;
    private readonly IEncryptionService _encryption;

    public SettingsController(CodeIndexDbContext context, IEncryptionService encryption)
    {
        _context = context;
        _encryption = encryption;
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? "anonymous";

    /// <summary>Get current user's settings (API keys are masked).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct = default)
    {
        var userId = GetUserId();
        var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        return Ok(new
        {
            hasEmbeddingKey = settings?.EmbeddingApiKey is not null,
            embeddingKeyMasked = MaskKey(settings?.EmbeddingApiKey),
            embeddingModel = settings?.EmbeddingModel,
            hasLlmKey = settings?.LlmApiKey is not null,
            llmKeyMasked = MaskKey(settings?.LlmApiKey),
        });
    }

    /// <summary>Update current user's settings.</summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings is null)
        {
            settings = new UserSettings
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.UserSettings.Add(settings);
        }

        if (request.EmbeddingApiKey is not null)
            settings.EmbeddingApiKey = _encryption.Encrypt(request.EmbeddingApiKey);

        if (request.EmbeddingModel is not null)
            settings.EmbeddingModel = request.EmbeddingModel;

        if (request.LlmApiKey is not null)
            settings.LlmApiKey = _encryption.Encrypt(request.LlmApiKey);

        settings.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new { message = "Settings updated" });
    }

    /// <summary>Delete current user's embedding API key.</summary>
    [HttpDelete("embedding-key")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteEmbeddingKey(CancellationToken ct = default)
    {
        var userId = GetUserId();
        var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings is not null)
        {
            settings.EmbeddingApiKey = null;
            settings.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new { message = "Embedding key removed" });
    }

    private string? MaskKey(string? encryptedKey)
    {
        if (encryptedKey is null) return null;
        var decrypted = _encryption.Decrypt(encryptedKey);
        if (string.IsNullOrEmpty(decrypted) || decrypted.Length < 8) return "****";
        return decrypted[..3] + "..." + decrypted[^4..];
    }
}

public class UpdateSettingsRequest
{
    public string? EmbeddingApiKey { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmApiKey { get; set; }
}
