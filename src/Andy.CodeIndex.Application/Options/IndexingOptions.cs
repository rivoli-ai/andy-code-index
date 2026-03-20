namespace Andy.CodeIndex.Application.Options;

public class IndexingOptions
{
    public string DataDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".andy-code-index");

    public int WorkerCount { get; set; } = 1;
    public int SearchLimit { get; set; } = 10;
}
