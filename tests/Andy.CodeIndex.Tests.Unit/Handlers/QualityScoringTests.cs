using Andy.CodeIndex.Infrastructure.Handlers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class QualityScoringTests
{
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
