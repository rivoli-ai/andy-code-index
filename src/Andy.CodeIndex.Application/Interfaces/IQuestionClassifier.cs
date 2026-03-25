using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IQuestionClassifier
{
    ClassificationResult Classify(string message);
    List<SuggestionDimension> GetSuggestions();
}

public class ClassificationResult
{
    public required string DimensionId { get; set; }
    public required string DimensionLabel { get; set; }
    public double Confidence { get; set; }
    public string? MatchedQuestionId { get; set; }
    public string? MatchedQuestionText { get; set; }
    public required EnrichmentSubtype[] RequiredEnrichments { get; set; }
    public required EnrichmentSubtype[] FallbackEnrichments { get; set; }
}

public class SuggestionDimension
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public required List<SuggestionQuestion> Questions { get; set; }
}

public class SuggestionQuestion
{
    public required string Id { get; set; }
    public required string Text { get; set; }
}
