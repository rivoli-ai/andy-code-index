using Andy.CodeIndex.Infrastructure.Handlers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class QualityScoringTests
{
    // --- EstimateQuality edge cases ---

    [Theory]
    [InlineData("no relevant information found", 0.1)]
    [InlineData("I cannot determine from the available code", 0.1)]
    [InlineData("could not find any database schema", 0.1)]
    [InlineData("insufficient context to analyze", 0.1)]
    public void EstimateQuality_VariousLowQualityPhrases_ReturnsLow(string content, double maxExpected)
    {
        BaseLlmEnrichmentHandler.EstimateQuality(content).Should().BeLessThanOrEqualTo(maxExpected + 0.2);
    }

    [Theory]
    [InlineData(50, 0.2)]
    [InlineData(150, 0.5)]
    [InlineData(500, 0.7)]
    [InlineData(1200, 0.85)]
    [InlineData(3000, 1.0)]
    public void EstimateQuality_VariousLengths_ScalesCorrectly(int length, double minExpected)
    {
        var content = new string('x', length);
        BaseLlmEnrichmentHandler.EstimateQuality(content).Should().BeGreaterThanOrEqualTo(minExpected);
    }

    [Fact]
    public void EstimateQuality_EmptyContent_ReturnsZero()
    {
        BaseLlmEnrichmentHandler.EstimateQuality("").Should().Be(0.0);
        BaseLlmEnrichmentHandler.EstimateQuality("  ").Should().Be(0.0);
    }

    [Fact]
    public void EstimateQuality_LowQualityShortContent_ReturnsVeryLow()
    {
        var content = "No database schema found in this repository.";
        BaseLlmEnrichmentHandler.EstimateQuality(content).Should().BeLessThanOrEqualTo(0.3);
    }

    [Fact]
    public void EstimateQuality_LowQualityLongerContent_ReturnsLow()
    {
        var content = new string('x', 600) + " Unable to determine the architecture of this project. " + new string('y', 200);
        BaseLlmEnrichmentHandler.EstimateQuality(content).Should().Be(0.3);
    }

    [Fact]
    public void EstimateQuality_VeryShortContent_ReturnsLow()
    {
        BaseLlmEnrichmentHandler.EstimateQuality("Short").Should().BeLessThanOrEqualTo(0.2);
    }

    [Fact]
    public void EstimateQuality_MediumContent_ReturnsMedium()
    {
        var content = new string('a', 500);
        var quality = BaseLlmEnrichmentHandler.EstimateQuality(content);
        quality.Should().BeGreaterThanOrEqualTo(0.5);
    }

    [Fact]
    public void EstimateQuality_LongSubstantiveContent_ReturnsHigh()
    {
        var content = new string('a', 2500);
        BaseLlmEnrichmentHandler.EstimateQuality(content).Should().Be(1.0);
    }

    [Fact]
    public void EstimateQuality_ModerateContent_ReturnsGood()
    {
        var content = new string('a', 1500);
        BaseLlmEnrichmentHandler.EstimateQuality(content).Should().BeGreaterThanOrEqualTo(0.8);
    }
}
