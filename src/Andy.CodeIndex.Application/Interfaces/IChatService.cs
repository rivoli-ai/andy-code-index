using Andy.CodeIndex.Application.DTOs;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, string? userId = null, CancellationToken ct = default);
    bool IsAvailable { get; }
}
