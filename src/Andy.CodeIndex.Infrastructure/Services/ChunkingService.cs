using System.Globalization;
using System.Text;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ChunkingService : IChunkingService
{
    private static readonly ChunkingOptions DefaultOptions = new();

    public List<CodeChunk> ChunkText(string content, string? filePath = null, ChunkingOptions? options = null)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        var opts = options ?? DefaultOptions;
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
}
