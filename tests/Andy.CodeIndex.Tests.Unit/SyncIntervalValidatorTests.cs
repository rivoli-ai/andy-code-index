using Andy.CodeIndex.Domain;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit;

public class SyncIntervalValidatorTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(120, true)]
    [InlineData(360, true)]
    [InlineData(720, true)]
    [InlineData(1440, true)]
    public void IsValid_AllowedValues_ReturnsTrue(int? value, bool expected)
    {
        SyncIntervalValidator.IsValid(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(180)]
    [InlineData(240)]
    [InlineData(480)]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(-60)]
    [InlineData(2880)]
    public void IsValid_DisallowedValues_ReturnsFalse(int value)
    {
        SyncIntervalValidator.IsValid(value).Should().BeFalse();
    }

    [Fact]
    public void AllowedValues_ContainsExpectedSet()
    {
        SyncIntervalValidator.AllowedValues.Should().BeEquivalentTo(
            new[] { 0, 15, 30, 60, 120, 360, 720, 1440 });
    }
}
