using Andy.CodeIndex.Domain.Enums;
using Pgvector;

namespace Andy.CodeIndex.Domain.Entities;

public class ContentEmbedding
{
    public Guid Id { get; set; }
    public Guid EnrichmentId { get; set; }
    public required Vector EmbeddingVector { get; set; }
    public IndexType IndexType { get; set; }
    public DateTime CreatedAt { get; set; }

    public Enrichment Enrichment { get; set; } = null!;
}
