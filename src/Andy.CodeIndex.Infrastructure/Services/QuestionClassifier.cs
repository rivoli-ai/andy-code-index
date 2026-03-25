using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Infrastructure.Services;

public class QuestionClassifier : IQuestionClassifier
{
    private readonly List<OntologyDimension> _dimensions;
    private readonly EnrichmentSubtype[] _allNonChunkSubtypes;

    public QuestionClassifier()
    {
        _dimensions = LoadOntology();
        _allNonChunkSubtypes = Enum.GetValues<EnrichmentSubtype>()
            .Where(s => s != EnrichmentSubtype.Chunk)
            .ToArray();
    }

    public ClassificationResult Classify(string message)
    {
        var words = Tokenize(message);
        if (words.Count == 0)
            return GeneralResult();

        var bestDimension = (OntologyDimension?)null;
        var bestScore = 0.0;
        var bestQuestion = (OntologyQuestion?)null;

        foreach (var dim in _dimensions)
        {
            var dimBestScore = 0.0;
            OntologyQuestion? dimBestQuestion = null;

            // Score each question: proportion of question keywords that appear in the message
            foreach (var q in dim.Questions)
            {
                if (q.Keywords.Count == 0) continue;
                var matched = q.Keywords.Count(k => words.Any(w =>
                    w.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                    w.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    k.Contains(w, StringComparison.OrdinalIgnoreCase)));
                var qScore = (double)matched / q.Keywords.Count;
                if (qScore > dimBestScore)
                {
                    dimBestScore = qScore;
                    dimBestQuestion = q;
                }
            }

            if (dimBestScore > bestScore)
            {
                bestScore = dimBestScore;
                bestDimension = dim;
                bestQuestion = dimBestQuestion;
            }
        }

        if (bestDimension == null || bestScore < 0.15)
            return GeneralResult();

        return new ClassificationResult
        {
            DimensionId = bestDimension.Id,
            DimensionLabel = bestDimension.Label,
            Confidence = Math.Min(bestScore, 1.0),
            MatchedQuestionId = bestQuestion?.Id,
            MatchedQuestionText = bestQuestion?.Text,
            RequiredEnrichments = ParseSubtypes(bestDimension.Enrichments),
            FallbackEnrichments = ParseSubtypes(bestDimension.FallbackEnrichments)
        };
    }

    public List<SuggestionDimension> GetSuggestions()
    {
        return _dimensions.Select(d => new SuggestionDimension
        {
            Id = d.Id,
            Label = d.Label,
            Questions = d.Questions
                .Where(q => q.Suggested)
                .Select(q => new SuggestionQuestion { Id = q.Id, Text = q.Text })
                .ToList()
        }).Where(d => d.Questions.Count > 0).ToList();
    }

    private ClassificationResult GeneralResult() => new()
    {
        DimensionId = "general",
        DimensionLabel = "General",
        Confidence = 0.0,
        RequiredEnrichments = _allNonChunkSubtypes,
        FallbackEnrichments = []
    };

    private static List<string> Tokenize(string message)
    {
        return message.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '\n', '\t', '(', ')', '[', ']', '{', '}', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .ToList();
    }

    private static EnrichmentSubtype[] ParseSubtypes(List<string> names)
    {
        return names
            .Select(n => Enum.TryParse<EnrichmentSubtype>(n, true, out var v) ? v : (EnrichmentSubtype?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
    }

    private static List<OntologyDimension> LoadOntology()
    {
        var assembly = typeof(QuestionClassifier).Assembly;
        // The JSON is embedded in the Api project, but we load from a well-known path at runtime
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Data", "question-ontology.json");

        // Try file system first (for runtime), then embedded resource (for tests)
        string json;
        if (File.Exists(jsonPath))
        {
            json = File.ReadAllText(jsonPath);
        }
        else
        {
            // Fallback: try to find it relative to the assembly
            var altPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? ".", "Data", "question-ontology.json");
            if (File.Exists(altPath))
                json = File.ReadAllText(altPath);
            else
                return [];
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var ontology = JsonSerializer.Deserialize<OntologyRoot>(json, options);
        return ontology?.Dimensions ?? [];
    }

    // --- Internal models for JSON deserialization ---
    private class OntologyRoot
    {
        public int Version { get; set; }
        public List<OntologyDimension> Dimensions { get; set; } = [];
    }

    private class OntologyDimension
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public List<string> Enrichments { get; set; } = [];
        public List<string> FallbackEnrichments { get; set; } = [];
        public List<OntologyQuestion> Questions { get; set; } = [];
    }

    private class OntologyQuestion
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public List<string> Keywords { get; set; } = [];
        public bool Suggested { get; set; }
    }
}
