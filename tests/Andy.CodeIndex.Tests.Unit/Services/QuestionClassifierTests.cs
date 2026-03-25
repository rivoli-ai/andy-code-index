using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class QuestionClassifierTests
{
    private readonly QuestionClassifier _classifier = new();

    [Theory]
    [InlineData("How is the folder structure and layout organized?", "structure")]
    [InlineData("What modules and packages make up the project structure?", "structure")]
    [InlineData("What languages are used?", "structure")]
    public void Classify_StructureQuestions_ReturnStructureDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
        result.RequiredEnrichments.Should().Contain(EnrichmentSubtype.Physical);
    }

    [Theory]
    [InlineData("What database schemas exist?", "data")]
    [InlineData("What database entities and table relationships exist?", "data")]
    public void Classify_DataQuestions_ReturnDataDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
        result.RequiredEnrichments.Should().Contain(EnrichmentSubtype.DatabaseSchema);
    }

    [Theory]
    [InlineData("What packages are used?", "dependencies")]
    [InlineData("What NuGet dependencies exist?", "dependencies")]
    public void Classify_DependencyQuestions_ReturnDependencyDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
        result.RequiredEnrichments.Should().Contain(EnrichmentSubtype.Dependencies);
    }

    [Theory]
    [InlineData("What changed recently?", "history")]
    [InlineData("What commits were made?", "history")]
    public void Classify_HistoryQuestions_ReturnHistoryDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
    }

    [Theory]
    [InlineData("How do I build and compile this project?", "howto")]
    [InlineData("What are the setup prerequisites and how do I run locally?", "howto")]
    public void Classify_HowToQuestions_ReturnHowToDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
        result.RequiredEnrichments.Should().Contain(EnrichmentSubtype.Cookbook);
    }

    [Theory]
    [InlineData("What API endpoints does this expose?", "documentation")]
    [InlineData("Where are the API contracts and swagger docs?", "documentation")]
    public void Classify_DocumentationQuestions_ReturnDocDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
    }

    [Theory]
    [InlineData("Who owns this repository?", "ownership")]
    [InlineData("Who are the maintainers?", "ownership")]
    public void Classify_OwnershipQuestions_ReturnOwnershipDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
    }

    [Theory]
    [InlineData("Where is authentication implemented?", "security")]
    [InlineData("How are secrets managed?", "security")]
    public void Classify_SecurityQuestions_ReturnSecurityDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
    }

    [Theory]
    [InlineData("What Docker container setup exists for deployment?", "operations")]
    [InlineData("What CI/CD deployment pipelines exist?", "operations")]
    public void Classify_OperationsQuestions_ReturnOpsDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
    }

    [Theory]
    [InlineData("What static analysis and lint tools are configured?", "quality")]
    [InlineData("What code complexity hotspots and cyclomatic complexity issues exist?", "quality")]
    public void Classify_QualityQuestions_ReturnQualityDimension(string message, string expectedDimension)
    {
        var result = _classifier.Classify(message);
        result.DimensionId.Should().Be(expectedDimension);
    }

    [Fact]
    public void Classify_EmptyMessage_ReturnsGeneral()
    {
        var result = _classifier.Classify("");
        result.DimensionId.Should().Be("general");
    }

    [Fact]
    public void Classify_VagueMessage_ReturnsGeneral()
    {
        var result = _classifier.Classify("hello");
        result.DimensionId.Should().Be("general");
    }

    [Fact]
    public void GetSuggestions_Returns10Dimensions()
    {
        var suggestions = _classifier.GetSuggestions();
        suggestions.Should().HaveCount(10);
    }

    [Fact]
    public void GetSuggestions_EachDimensionHas6Questions()
    {
        var suggestions = _classifier.GetSuggestions();
        foreach (var dim in suggestions)
        {
            dim.Questions.Should().HaveCount(6, $"dimension {dim.Id} should have 6 suggested questions");
        }
    }

    [Fact]
    public void Classify_ReturnsConfidenceAboveZero_ForMatchedQuestions()
    {
        var result = _classifier.Classify("What are the main modules and how are they organized?");
        result.Confidence.Should().BeGreaterThan(0);
        result.DimensionId.Should().NotBe("general");
    }
}
