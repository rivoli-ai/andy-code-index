namespace Andy.CodeIndex.Application.Options;

public class ChunkingOptions
{
    public int Size { get; set; } = 1500;
    public int Overlap { get; set; } = 200;
    public int MinSize { get; set; } = 50;
}
