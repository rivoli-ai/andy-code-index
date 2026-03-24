using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    private readonly EmbeddingOptions _embeddingOptions;

    public SettingsController(
        CodeIndexDbContext context,
        IEncryptionService encryption,
        IOptions<EmbeddingOptions> embeddingOptions)
    {
        _context = context;
        _encryption = encryption;
        _embeddingOptions = embeddingOptions.Value;
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? "anonymous";

    /// <summary>Get current user's settings (API keys masked, shows source).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct = default)
    {
        var userId = GetUserId();
        var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        var hasUserKey = settings?.EmbeddingApiKey is not null;
        var hasSystemKey = _embeddingOptions.IsConfigured;

        return Ok(new
        {
            embedding = new
            {
                hasKey = hasUserKey || hasSystemKey,
                source = hasUserKey ? "user" : hasSystemKey ? "system" : "none",
                maskedKey = hasUserKey ? MaskKey(settings!.EmbeddingApiKey!) : hasSystemKey ? MaskPlainKey(_embeddingOptions.ApiKey!) : null,
                model = settings?.EmbeddingModel ?? _embeddingOptions.Model,
                configuredAt = settings?.UpdatedAt,
            },
            llm = new
            {
                hasKey = settings?.LlmApiKey is not null,
                maskedKey = settings?.LlmApiKey is not null ? MaskKey(settings.LlmApiKey) : null,
            }
        });
    }

    /// <summary>Update current user's settings with audit trail.</summary>
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
        {
            var oldMasked = settings.EmbeddingApiKey is not null ? MaskKey(settings.EmbeddingApiKey) : null;
            settings.EmbeddingApiKey = _encryption.Encrypt(request.EmbeddingApiKey);
            LogChange(userId, "EmbeddingApiKey", oldMasked, MaskPlainKey(request.EmbeddingApiKey),
                oldMasked is null ? "set" : "updated");
        }

        if (request.EmbeddingModel is not null)
        {
            LogChange(userId, "EmbeddingModel", settings.EmbeddingModel, request.EmbeddingModel,
                settings.EmbeddingModel is null ? "set" : "updated");
            settings.EmbeddingModel = request.EmbeddingModel;
        }

        if (request.LlmApiKey is not null)
        {
            var oldMasked = settings.LlmApiKey is not null ? MaskKey(settings.LlmApiKey) : null;
            settings.LlmApiKey = _encryption.Encrypt(request.LlmApiKey);
            LogChange(userId, "LlmApiKey", oldMasked, MaskPlainKey(request.LlmApiKey),
                oldMasked is null ? "set" : "updated");
        }

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

        if (settings?.EmbeddingApiKey is not null)
        {
            LogChange(userId, "EmbeddingApiKey", MaskKey(settings.EmbeddingApiKey), null, "removed");
            settings.EmbeddingApiKey = null;
            settings.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new { message = "Embedding key removed, falling back to system key" });
    }

    /// <summary>Get settings change history for the current user.</summary>
    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var logs = await _context.SettingsChangeLogs
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new
            {
                l.Field,
                l.Action,
                l.OldValue,
                l.NewValue,
                l.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(logs);
    }

    private void LogChange(string userId, string field, string? oldValue, string? newValue, string action)
    {
        _context.SettingsChangeLogs.Add(new SettingsChangeLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Action = action,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>Mask an encrypted key (decrypt first, then mask).</summary>
    private string? MaskKey(string? encryptedKey)
    {
        if (string.IsNullOrEmpty(encryptedKey)) return null;
        try
        {
            var decrypted = _encryption.Decrypt(encryptedKey);
            return MaskPlainKey(decrypted);
        }
        catch
        {
            return "***";
        }
    }

    /// <summary>Mask a plaintext key: ***...XXXX (last 4 chars).</summary>
    private static string? MaskPlainKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.Length <= 4) return "***";
        return $"***...{key[^4..]}";
    }
}

public class UpdateSettingsRequest
{
    public string? EmbeddingApiKey { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmApiKey { get; set; }
}
