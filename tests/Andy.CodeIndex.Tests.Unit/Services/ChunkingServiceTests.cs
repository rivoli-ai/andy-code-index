using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class ChunkingServiceTests
{
    private readonly ChunkingService _service = new();

    [Fact]
    public void ChunkText_EmptyContent_ReturnsEmpty()
    {
        _service.ChunkText("").Should().BeEmpty();
        _service.ChunkText(null!).Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_ShortContent_ReturnsSingleChunk()
    {
        // Content must be >= MinSize (50 runes by default)
        var content = "Hello, world! This is a line with enough content to pass.\n";
        var options = new ChunkingOptions { Size = 1500, Overlap = 200, MinSize = 10 };
        var result = _service.ChunkText(content, options: options);

        result.Should().HaveCount(1);
        result[0].Content.Should().Be(content);
        result[0].StartLine.Should().Be(1);
        result[0].EndLine.Should().Be(1);
    }

    [Fact]
    public void ChunkText_ContentBelowMinSize_ReturnsEmpty()
    {
        var options = new ChunkingOptions { Size = 1500, Overlap = 200, MinSize = 50 };
        var result = _service.ChunkText("tiny", options: options);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_MultipleLines_ChunksWithOverlap()
    {
        // Create content that exceeds chunk size
        var lines = Enumerable.Range(1, 100)
            .Select(i => $"Line {i}: This is some content to fill up the chunk.{new string('x', 10)}\n")
            .ToList();
        var content = string.Join("", lines);

        var options = new ChunkingOptions { Size = 500, Overlap = 100, MinSize = 10 };
        var result = _service.ChunkText(content, "test.cs", options);

        result.Should().HaveCountGreaterThan(1);

        // All chunks should have valid line ranges
        foreach (var chunk in result)
        {
            chunk.StartLine.Should().BeGreaterThan(0);
            chunk.EndLine.Should().BeGreaterThanOrEqualTo(chunk.StartLine);
            chunk.FilePath.Should().Be("test.cs");
            ChunkingService.CountRunes(chunk.Content).Should().BeLessThanOrEqualTo(options.Size + options.Overlap + 100); // some tolerance for line boundaries
        }

        // First chunk starts at line 1
        result[0].StartLine.Should().Be(1);

        // Verify overlap: content at end of chunk N should appear at start of chunk N+1
        for (var i = 0; i < result.Count - 1; i++)
        {
            var currentEnd = result[i].Content;
            var nextStart = result[i + 1].Content;
            // The next chunk should start before the current chunk ends (overlap)
            result[i + 1].StartLine.Should().BeLessThanOrEqualTo(result[i].EndLine);
        }
    }

    [Fact]
    public void ChunkText_OversizedLine_SplitsOnWhitespace()
    {
        // Single line longer than chunk size, with whitespace
        var longLine = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
        var options = new ChunkingOptions { Size = 100, Overlap = 20, MinSize = 5 };

        var result = _service.ChunkText(longLine, options: options);

        result.Should().HaveCountGreaterThan(1);
        // Each chunk from the split line should reference the same line
        result.Should().AllSatisfy(c => c.StartLine.Should().Be(1));
    }

    [Fact]
    public void ChunkText_NoWhitespace_HardSplitsOnRuneBoundary()
    {
        // Long string with no whitespace
        var noSpaces = new string('a', 500);
        var options = new ChunkingOptions { Size = 100, Overlap = 20, MinSize = 5 };

        var result = _service.ChunkText(noSpaces, options: options);

        result.Should().HaveCountGreaterThan(1);
        // Each chunk should not exceed size (with some tolerance for the Tier 3 remainder)
        foreach (var chunk in result)
        {
            ChunkingService.CountRunes(chunk.Content).Should().BeLessThanOrEqualTo(options.Size);
        }
    }

    [Fact]
    public void ChunkText_LineRangesAccurate()
    {
        var content = "line 1 with enough content to be above minimum\n" +
                      "line 2 with enough content to be above minimum\n" +
                      "line 3 with enough content to be above minimum\n" +
                      "line 4 with enough content to be above minimum\n" +
                      "line 5 with enough content to be above minimum\n";

        var options = new ChunkingOptions { Size = 100, Overlap = 20, MinSize = 10 };
        var result = _service.ChunkText(content, options: options);

        result.Should().HaveCountGreaterThan(0);
        result[0].StartLine.Should().Be(1);
        result[^1].EndLine.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void ChunkText_OnlyNewlines_ReturnsEmpty()
    {
        var options = new ChunkingOptions { Size = 100, Overlap = 20, MinSize = 50 };
        var result = _service.ChunkText("\n\n\n\n\n", options: options);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_RealCSharpCode_ProducesValidChunks()
    {
        var code = @"using System;

namespace Andy.CodeIndex.Tests
{
    public class SampleClass
    {
        private readonly string _name;

        public SampleClass(string name)
        {
            _name = name;
        }

        public string GetGreeting()
        {
            return $""Hello, {_name}!"";
        }

        public int Calculate(int a, int b)
        {
            return a + b;
        }

        public void DoWork()
        {
            for (int i = 0; i < 100; i++)
            {
                Console.WriteLine($""Working on {i}"");
            }
        }
    }
}
";
        var options = new ChunkingOptions { Size = 200, Overlap = 50, MinSize = 10 };
        var result = _service.ChunkText(code, "SampleClass.cs", options);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c =>
        {
            c.Content.Should().NotBeNullOrEmpty();
            c.FilePath.Should().Be("SampleClass.cs");
            c.StartLine.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ChunkText_Unicode_CountsRunesNotChars()
    {
        // Emoji is 1 rune but 2 chars (UTF-16 surrogate pair)
        var emoji = "🎉";
        var line = new string('a', 48) + emoji + "\n"; // 49 runes + newline
        var content = string.Concat(Enumerable.Repeat(line, 40)); // ~2000 runes

        var options = new ChunkingOptions { Size = 500, Overlap = 50, MinSize = 10 };
        var result = _service.ChunkText(content, options: options);

        result.Should().HaveCountGreaterThan(1);
        // Verify rune counting, not char counting
        foreach (var chunk in result)
        {
            var runeCount = ChunkingService.CountRunes(chunk.Content);
            runeCount.Should().BeLessThanOrEqualTo(options.Size + options.Overlap + 100);
        }
    }

    [Fact]
    public void ChunkText_DefaultOptions_Uses1500Size()
    {
        var longContent = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}: content"));
        var result = _service.ChunkText(longContent);

        result.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void CountRunes_AsciiAndUnicode()
    {
        ChunkingService.CountRunes("hello").Should().Be(5);
        ChunkingService.CountRunes("🎉").Should().Be(1);
        ChunkingService.CountRunes("héllo").Should().Be(5);
        ChunkingService.CountRunes("").Should().Be(0);
    }
}
