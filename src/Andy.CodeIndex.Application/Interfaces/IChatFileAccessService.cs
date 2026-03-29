namespace Andy.CodeIndex.Application.Interfaces;

public interface IChatFileAccessService
{
    Task<ChatFileContent> FetchFileForChatAsync(
        Guid repositoryId,
        string gitRef,
        string filePath,
        string? userId = null,
        CancellationToken ct = default);
}

public class ChatFileContent
{
    public string? Content { get; set; }
    public required string FilePath { get; set; }
    public string? ResolvedSha { get; set; }
    public long Size { get; set; }
    public string? Language { get; set; }
    public bool IsBinary { get; set; }
    public string? Error { get; set; }
    public bool IsSuccess => Error is null;
}
