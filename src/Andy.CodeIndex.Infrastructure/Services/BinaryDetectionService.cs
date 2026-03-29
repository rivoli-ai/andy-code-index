using Andy.CodeIndex.Application.Interfaces;

namespace Andy.CodeIndex.Infrastructure.Services;

public class BinaryDetectionService : IBinaryDetectionService
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Executables / compiled
        ".exe", ".dll", ".so", ".dylib", ".bin", ".com",
        ".class", ".pyc", ".pyo", ".o", ".obj", ".pdb", ".lib", ".a",

        // Archives
        ".zip", ".tar", ".gz", ".bz2", ".xz", ".7z", ".rar",
        ".jar", ".war", ".ear", ".nupkg",

        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".tiff", ".tif",
        ".webp", ".psd", ".ai", ".eps",

        // Audio / Video
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv", ".flac", ".ogg", ".wmv",

        // Fonts
        ".woff", ".woff2", ".ttf", ".eot", ".otf",

        // Documents (binary)
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",

        // Database
        ".db", ".sqlite", ".mdb",
    };

    public (bool IsBinary, string? Reason) IsBinary(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return (false, null);

        if (BinaryExtensions.Contains(ext))
            return (true, $"Extension '{ext}' is a known binary format");

        return (false, null);
    }
}
