namespace Andy.CodeIndex.Application.DTOs;

public class CommitComparisonDto
{
    public required string From { get; set; }
    public required string To { get; set; }
    public List<EnrichmentDto> Added { get; set; } = new();
    public List<EnrichmentDto> Removed { get; set; } = new();
    public List<EnrichmentChangeDto> Changed { get; set; } = new();
}

public class EnrichmentChangeDto
{
    public required EnrichmentDto From { get; set; }
    public required EnrichmentDto To { get; set; }
}
