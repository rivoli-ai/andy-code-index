namespace Andy.CodeIndex.Application.Options;

public class ChatFileAccessOptions
{
    public const string SectionName = "Chat:FileAccess";

    public bool Enabled { get; set; } = true;
    public int MaxFileSizeBytes { get; set; } = 102400; // 100KB
    public int MaxFilesPerTurn { get; set; } = 3;
    public int MaxIterations { get; set; } = 3;
}
