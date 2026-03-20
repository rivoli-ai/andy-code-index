using Andy.CodeIndex.Domain.Enums;
using NpgsqlTypes;

namespace Andy.CodeIndex.Domain.Entities;

public class Enrichment
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid? CommitId { get; set; }
    public EnrichmentType Type { get; set; }
    public EnrichmentSubtype Subtype { get; set; }
    public string? Title { get; set; }
    public required string Content { get; set; }
    public string? FilePath { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Language { get; set; }
    public NpgsqlTsVector? SearchVector { get; set; }
    public DateTime CreatedAt { get; set; }

    public Repository Repository { get; set; } = null!;
    public Commit? Commit { get; set; }
    public ICollection<ContentEmbedding> Embeddings { get; set; } = new List<ContentEmbedding>();
    public ICollection<ChunkLineRange> LineRanges { get; set; } = new List<ChunkLineRange>();
}
