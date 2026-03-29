namespace Andy.CodeIndex.Application.Options;

public class FileFilterOptions
{
    public List<string> SkipExtensions { get; set; } =
    [
        ".exe", ".dll", ".so", ".dylib", ".bin",
        ".zip", ".tar", ".gz", ".jar",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".tiff",
        ".mp3", ".mp4", ".wav",
        ".woff", ".woff2", ".ttf", ".eot",
        ".pyc", ".class", ".o", ".obj", ".pdb"
    ];

    public List<string> SkipPatterns { get; set; } =
    [
        "node_modules/**",
        ".git/**",
        "vendor/**",
        "bin/**",
        "obj/**",
        "dist/**",
        "build/**",
        "*.min.js",
        "*.min.css",
        "package-lock.json",
        "yarn.lock"
    ];

    public long MaxFileSizeBytes { get; set; } = 1_048_576; // 1 MB
}
