using System.Globalization;
using System.Text;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ChunkingService : IChunkingService
{
    private static readonly ChunkingOptions DefaultOptions = new();

    // Extensions whose language has a structural parser (Roslyn / Acornima).
    private static readonly Dictionary<string, string> StructuralLanguagesByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".js"] = "javascript",
            [".jsx"] = "javascript",
            [".mjs"] = "javascript",
            [".cjs"] = "javascript",
        };

    private readonly ICodeAnalysisService _analysis;

    // Optional dependency so existing `new ChunkingService()` usages (and tests)
    // keep working; DI supplies the registered analyzer.
    public ChunkingService(ICodeAnalysisService? analysis = null)
        => _analysis = analysis ?? new CodeAnalysisService();

    public List<CodeChunk> ChunkText(string content, string? filePath = null, ChunkingOptions? options = null)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        var opts = options ?? DefaultOptions;

        // Structure-aware path: align chunk starts to declaration boundaries so a
        // function/class is not split mid-body, and prepend file/type context to
        // each chunk to ground the embedding (story #249). Falls back to size-based
        // chunking when the language has no parser or yields no boundaries.
        if (filePath is not null &&
            StructuralLanguagesByExtension.TryGetValue(Path.GetExtension(filePath), out var lang))
        {
            var boundaries = _analysis.GetStructuralBoundaries(content, lang);
            if (boundaries.Count > 0)
                return ChunkStructured(content, filePath, boundaries, opts);
        }

        return ChunkBySize(content, filePath, opts);
    }

    private static List<CodeChunk> ChunkBySize(string content, string? filePath, ChunkingOptions opts)
    {
        var lines = SplitLines(content);
        var chunks = new List<CodeChunk>();

        var currentChunk = new StringBuilder();
        var currentRuneCount = 0;
        var currentStartLine = 1;
        var currentByteOffset = 0;
        var chunkStartByteOffset = 0;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineRuneCount = CountRunes(line);

            // Would adding this line exceed the size limit?
            if (currentRuneCount + lineRuneCount > opts.Size && currentRuneCount > 0)
            {
                // Emit current chunk
                EmitChunk(chunks, currentChunk, currentStartLine, lineIndex, filePath, chunkStartByteOffset, opts.MinSize);

                // Overlap: carry trailing whole lines within overlap budget
                var overlapResult = GetOverlapLines(lines, lineIndex - 1, currentStartLine - 1, opts.Overlap);
                currentChunk.Clear();
                currentChunk.Append(overlapResult.Text);
                currentRuneCount = overlapResult.RuneCount;
                currentStartLine = lineIndex + 1 - overlapResult.LineCount;
                chunkStartByteOffset = currentByteOffset - Encoding.UTF8.GetByteCount(overlapResult.Text);
            }

            if (lineRuneCount > opts.Size)
            {
                // Tier 2 & 3: split oversized lines
                SplitOversizedLine(chunks, line, lineIndex + 1, filePath,
                    ref currentChunk, ref currentRuneCount, ref currentStartLine,
                    ref chunkStartByteOffset, currentByteOffset, opts);
            }
            else
            {
                currentChunk.Append(line);
                currentRuneCount += lineRuneCount;
            }

            currentByteOffset += Encoding.UTF8.GetByteCount(line);
        }

        // Emit remaining content
        if (currentRuneCount > 0)
        {
            EmitChunk(chunks, currentChunk, currentStartLine, lines.Count, filePath, chunkStartByteOffset, opts.MinSize);
        }

        return chunks;
    }

    private static void SplitOversizedLine(
        List<CodeChunk> chunks, string line, int lineNumber, string? filePath,
        ref StringBuilder currentChunk, ref int currentRuneCount, ref int currentStartLine,
        ref int chunkStartByteOffset, int lineByteOffset, ChunkingOptions opts)
    {
        // First emit any accumulated content
        if (currentRuneCount > 0)
        {
            EmitChunk(chunks, currentChunk, currentStartLine, lineNumber - 1, filePath, chunkStartByteOffset, opts.MinSize);
            currentChunk.Clear();
            currentRuneCount = 0;
        }

        // Tier 2: try splitting on whitespace
        var remaining = line;
        var segmentStart = lineByteOffset;

        while (CountRunes(remaining) > opts.Size)
        {
            var splitPoint = FindWhitespaceSplit(remaining, opts.Size);
            if (splitPoint <= 0)
            {
                // Tier 3: hard split on rune boundary
                splitPoint = opts.Size;
            }

            var segment = SubstringByRunes(remaining, 0, splitPoint);
            chunks.Add(new CodeChunk
            {
                Content = segment,
                StartLine = lineNumber,
                EndLine = lineNumber,
                FilePath = filePath,
                ByteOffset = segmentStart
            });

            segmentStart += Encoding.UTF8.GetByteCount(segment);
            remaining = SubstringByRunes(remaining, splitPoint, CountRunes(remaining) - splitPoint);
        }

        // Put the remainder into the current chunk for potential merging
        currentChunk.Append(remaining);
        currentRuneCount = CountRunes(remaining);
        currentStartLine = lineNumber;
        chunkStartByteOffset = segmentStart;
    }

    private static void EmitChunk(
        List<CodeChunk> chunks, StringBuilder content, int startLine, int endLine,
        string? filePath, int byteOffset, int minSize)
    {
        var text = content.ToString();
        if (CountRunes(text) < minSize)
            return;

        chunks.Add(new CodeChunk
        {
            Content = text,
            StartLine = startLine,
            EndLine = endLine,
            FilePath = filePath,
            ByteOffset = byteOffset
        });
    }

    private static OverlapResult GetOverlapLines(List<string> lines, int endLineIndex, int startLineIndex, int overlapBudget)
    {
        var sb = new StringBuilder();
        var runeCount = 0;
        var lineCount = 0;

        // Walk backward from endLineIndex, adding whole lines within budget
        for (var i = endLineIndex; i >= startLineIndex; i--)
        {
            var lineRunes = CountRunes(lines[i]);
            if (runeCount + lineRunes > overlapBudget && lineCount > 0)
                break;

            lineCount++;
            runeCount += lineRunes;
        }

        // Build overlap text from the trailing lines
        var overlapStart = endLineIndex - lineCount + 1;
        for (var i = overlapStart; i <= endLineIndex; i++)
        {
            sb.Append(lines[i]);
        }

        return new OverlapResult(sb.ToString(), runeCount, lineCount);
    }

    private static int FindWhitespaceSplit(string text, int maxRunes)
    {
        // Find the last whitespace position within maxRunes
        var lastWhitespace = -1;
        var runeIndex = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (runeIndex >= maxRunes)
                break;

            if (Rune.IsWhiteSpace(rune))
                lastWhitespace = runeIndex;

            runeIndex++;
        }

        return lastWhitespace > 0 ? lastWhitespace + 1 : -1;
    }

    internal static int CountRunes(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }

    private static string SubstringByRunes(string text, int start, int length)
    {
        var sb = new StringBuilder();
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (index >= start + length)
                break;
            if (index >= start)
                sb.Append(rune.ToString());
            index++;
        }
        return sb.ToString();
    }

    private static List<string> SplitLines(string content)
    {
        // Split preserving line endings so we can measure accurately
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lines.Add(content[start..(i + 1)]);
                start = i + 1;
            }
        }
        if (start < content.Length)
            lines.Add(content[start..]);

        return lines;
    }

    private record OverlapResult(string Text, int RuneCount, int LineCount);

    // ----- Structure-aware chunking (story #249) -----

    private static List<CodeChunk> ChunkStructured(
        string content, string filePath, IReadOnlyList<StructuralBoundary> boundaries, ChunkingOptions opts)
    {
        var lines = SplitLines(content);
        var lineByteOffsets = ComputeLineByteOffsets(lines);
        var segments = BuildSegments(lines.Count, boundaries);

        var chunks = new List<CodeChunk>();
        var buf = new StringBuilder();
        int bufStart = 0, bufEnd = 0, bufRunes = 0;
        var bufContext = "";

        void Flush()
        {
            if (bufRunes == 0) return;
            chunks.Add(MakeStructuredChunk(buf.ToString(), filePath, bufContext, bufStart, bufEnd, lineByteOffsets));
            buf.Clear();
            bufRunes = 0;
        }

        foreach (var (start, end, context) in segments)
        {
            var text = JoinLines(lines, start, end);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var runes = CountRunes(text);

            // A single declaration larger than the size limit is flushed on its own
            // and then size-split, so we never merge a giant body with its neighbors.
            if (runes > opts.Size)
            {
                Flush();
                foreach (var sub in ChunkBySize(text, filePath, opts))
                {
                    chunks.Add(MakeStructuredChunk(
                        sub.Content, filePath, context,
                        start + sub.StartLine - 1, start + sub.EndLine - 1, lineByteOffsets));
                }
                continue;
            }

            // Adding this declaration would overflow the current chunk: start a new one.
            if (bufRunes > 0 && bufRunes + runes > opts.Size)
                Flush();

            if (bufRunes == 0)
            {
                bufStart = start;
                bufContext = context;
            }
            else if (bufContext.Length == 0 && context.Length > 0)
            {
                // A leading file-level segment merged with a declaration: adopt the
                // declaration's context for the chunk header.
                bufContext = context;
            }
            buf.Append(text);
            bufRunes += runes;
            bufEnd = end;
        }

        Flush();
        return chunks;
    }

    // Partition lines [1..lineCount] at the boundary lines into contiguous segments.
    // Lines before the first boundary become a leading file-context segment.
    private static List<(int Start, int End, string Context)> BuildSegments(
        int lineCount, IReadOnlyList<StructuralBoundary> boundaries)
    {
        var segments = new List<(int, int, string)>();
        var first = Math.Clamp(boundaries[0].Line, 1, lineCount + 1);
        if (first > 1)
            segments.Add((1, first - 1, ""));

        for (var i = 0; i < boundaries.Count; i++)
        {
            var start = Math.Clamp(boundaries[i].Line, 1, lineCount);
            var end = i + 1 < boundaries.Count
                ? Math.Clamp(boundaries[i + 1].Line - 1, start, lineCount)
                : lineCount;
            if (end >= start)
                segments.Add((start, end, boundaries[i].Context));
        }
        return segments;
    }

    private static CodeChunk MakeStructuredChunk(
        string body, string filePath, string context, int startLine, int endLine, int[] lineByteOffsets)
    {
        var header = context.Length > 0 ? $"// {filePath} — {context}\n" : $"// {filePath}\n";
        var offset = startLine >= 1 && startLine <= lineByteOffsets.Length ? lineByteOffsets[startLine - 1] : 0;
        return new CodeChunk
        {
            Content = header + body,
            StartLine = startLine,
            EndLine = endLine,
            FilePath = filePath,
            ByteOffset = offset
        };
    }

    private static string JoinLines(List<string> lines, int start, int end)
    {
        var sb = new StringBuilder();
        for (var i = start; i <= end && i <= lines.Count; i++)
            sb.Append(lines[i - 1]);
        return sb.ToString();
    }

    private static int[] ComputeLineByteOffsets(List<string> lines)
    {
        var offsets = new int[lines.Count];
        var running = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            offsets[i] = running;
            running += Encoding.UTF8.GetByteCount(lines[i]);
        }
        return offsets;
    }
}
