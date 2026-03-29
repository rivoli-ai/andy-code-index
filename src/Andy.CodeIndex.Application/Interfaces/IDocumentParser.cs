namespace Andy.CodeIndex.Application.Interfaces;

public interface IDocumentParser
{
    bool CanParse(string extension);
    Task<ParsedDocument> ParseAsync(Stream content, string filePath, CancellationToken ct = default);
}

public record ParsedDocument
{
    public required string TextContent { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public int? PageCount { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public List<DocumentSection> Sections { get; init; } = new();
}

public record DocumentSection
{
    public required string Content { get; init; }
    public int? PageNumber { get; init; }
    public string? Title { get; init; }
}
