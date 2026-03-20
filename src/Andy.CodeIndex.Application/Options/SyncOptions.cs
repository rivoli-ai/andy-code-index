namespace Andy.CodeIndex.Application.Options;

public class SyncOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 1800;
}
