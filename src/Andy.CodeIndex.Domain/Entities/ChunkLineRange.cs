namespace Andy.CodeIndex.Domain.Entities;

public class ChunkLineRange
{
    public Guid Id { get; set; }
    public Guid EnrichmentId { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public Enrichment Enrichment { get; set; } = null!;
}
