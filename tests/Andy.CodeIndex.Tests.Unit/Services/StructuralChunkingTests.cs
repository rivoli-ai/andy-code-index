using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// Structure-aware chunking (story #249): chunks align to declaration boundaries
/// (Roslyn for C#, Acornima for JS) instead of arbitrary size cuts, and each
/// chunk carries a "// file — Context" header to ground the embedding. Falls back
/// to size-based chunking for unparsed languages.
/// </summary>
public class StructuralChunkingTests
{
    private readonly ChunkingService _service = new();

    [Fact]
    public void CSharp_SplitsAtMethodBoundaries_WithContextHeader()
    {
        var code = @"namespace Demo
{
    public class Service
    {
        public int Add(int a, int b) { return a + b; }
        public int Subtract(int a, int b) { return a - b; }
        public int Multiply(int a, int b) { return a * b; }
    }
}
";
        // Small size forces one chunk per method rather than one merged chunk.
        var options = new ChunkingOptions { Size = 80, Overlap = 0, MinSize = 1 };
        var result = _service.ChunkText(code, "Service.cs", options);

        result.Should().HaveCountGreaterThan(2);
        result.Should().OnlyContain(c => c.Content.StartsWith("// Service.cs"));

        // The chunk that begins at the Add method (line 5) is grounded with its context.
        var addChunk = result.Should().ContainSingle(c => c.StartLine == 5).Subject;
        addChunk.Content.Should().Contain("Service.Add");
        addChunk.Content.Should().Contain("a + b");

        // A method is not split across chunks: the Subtract body stays whole.
        result.Should().ContainSingle(c => c.Content.Contains("a - b"))
            .Which.Content.Should().Contain("Service.Subtract");
    }

    [Fact]
    public void CSharp_SmallFile_FitsInOneChunk_StillHasHeader()
    {
        var code = "namespace N { public class A { public void M() { } } }\n";
        var result = _service.ChunkText(code, "A.cs");

        result.Should().ContainSingle();
        result[0].Content.Should().StartWith("// A.cs");
        result[0].Content.Should().Contain("public class A");
    }

    [Fact]
    public void JavaScript_SplitsClassMethodsAndArrowExports()
    {
        var code = @"export class Calc {
  add(a, b) { return a + b; }
  subtract(a, b) { return a - b; }
}
export const helper = (x) => x * 2;
";
        var options = new ChunkingOptions { Size = 40, Overlap = 0, MinSize = 1 };
        var result = _service.ChunkText(code, "calc.js", options);

        result.Should().OnlyContain(c => c.Content.StartsWith("// calc.js"));
        result.Should().Contain(c => c.Content.Contains("Calc.add") && c.Content.Contains("a + b"));
        result.Should().Contain(c => c.Content.Contains("a - b"));
    }

    [Fact]
    public void UnparsedLanguage_FallsBackToSizeBased_NoHeader()
    {
        // No structural parser for .txt → size-based path, content unchanged.
        var code = "just some plain text\nwith a couple of lines\n";
        var options = new ChunkingOptions { Size = 1500, Overlap = 0, MinSize = 1 };
        var result = _service.ChunkText(code, "notes.txt", options);

        result.Should().ContainSingle();
        result[0].Content.Should().NotStartWith("//");
        result[0].Content.Should().Be(code);
    }

    [Fact]
    public void NonStructuralCSharpContent_FallsBackGracefully()
    {
        // A .cs path whose content has no type/method declarations yields no
        // boundaries, so the chunker falls back to size-based without throwing.
        var code = "// just a comment\nint x = 1;\n";
        var options = new ChunkingOptions { Size = 1500, Overlap = 0, MinSize = 1 };
        var act = () => _service.ChunkText(code, "loose.cs", options);

        act.Should().NotThrow();
        act().Should().NotBeEmpty();
    }
}
