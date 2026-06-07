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

    // Content-based detection for files that slip past the extension allowlist
    // (renamed or extensionless binaries). A NUL byte in the leading bytes is the
    // same heuristic git uses to classify a blob as binary. (story #258)
    internal static bool ContentLooksBinary(string content)
    {
        var limit = Math.Min(content.Length, 8000);
        for (var i = 0; i < limit; i++)
            if (content[i] == '\0')
                return true;
        return false;
    }
}
