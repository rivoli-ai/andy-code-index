namespace Andy.CodeIndex.Domain.Enums;

public enum EnrichmentSubtype
{
    // Architecture
    Physical,
    DatabaseSchema,

    // Development
    Chunk,
    Snippet,
    SnippetSummary,
    Example,
    ExampleSummary,

    // History
    CommitDescription,

    // Usage
    Cookbook,
    APIDocs,
    Wiki
}
