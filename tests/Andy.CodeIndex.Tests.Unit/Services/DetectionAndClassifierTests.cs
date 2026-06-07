using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// Covers the detection/classifier fixes in epic #245: missing language
/// extensions (#258), content-based binary detection (#258), and the hardened
/// QuestionClassifier keyword matching (#259).
/// </summary>
public class DetectionAndClassifierTests
{
    [Theory]
    [InlineData("module.cts", "typescript")]
    [InlineData("module.mts", "typescript")]
    [InlineData("module.cjs", "javascript")]
    [InlineData("app.ts", "typescript")]
    [InlineData("app.mjs", "javascript")]
    [InlineData("Program.cs", "csharp")]
    [InlineData("notes.unknownext", null)]
    [InlineData("Dockerfile", null)]
    public void DetectLanguage_MapsExtensions_IncludingNewlyAdded(string path, string? expected)
    {
        GitService.DetectLanguage(path).Should().Be(expected);
    }

    [Fact]
    public void ContentLooksBinary_DetectsNulByte_ButNotPlainText()
    {
        BinaryDetectionService.ContentLooksBinary("public class A { }\nint x = 1;").Should().BeFalse();
        BinaryDetectionService.ContentLooksBinary("PK\0\0binary").Should().BeTrue();
        BinaryDetectionService.ContentLooksBinary("").Should().BeFalse();
    }

    [Theory]
    // Exact and legitimate prefix matches still work.
    [InlineData("test", "test", true)]
    [InlineData("authentication", "auth", true)]
    [InlineData("auth", "authentication", true)]
    // Short-token false positives the old bidirectional Contains produced are gone.
    [InlineData("go", "google", false)]
    [InlineData("ci", "specific", false)]
    [InlineData("go", "goroutine", false)]
    public void KeywordMatches_AvoidsShortSubstringFalsePositives(string word, string keyword, bool expected)
    {
        QuestionClassifier.KeywordMatches(word, keyword).Should().Be(expected);
    }
}
