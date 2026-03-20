using Andy.CodeIndex.Application.Options;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IChunkingService
{
    List<CodeChunk> ChunkText(string content, string? filePath = null, ChunkingOptions? options = null);
}

public class CodeChunk
{
    public required string Content { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? FilePath { get; set; }
    public int ByteOffset { get; set; }
}
