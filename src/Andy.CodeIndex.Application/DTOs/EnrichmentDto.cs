using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.DTOs;

public class EnrichmentDto
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string? RepositoryName { get; set; }
    public Guid? CommitId { get; set; }
    public EnrichmentType Type { get; set; }
    public EnrichmentSubtype Subtype { get; set; }
    public string? Title { get; set; }
    public required string Content { get; set; }
    public string? FilePath { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Language { get; set; }
    public double Quality { get; set; }
    public DateTime CreatedAt { get; set; }
}
