using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public partial class ChatFileAccessService : IChatFileAccessService
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IndexingOptions _indexingOptions;
    private readonly ChatFileAccessOptions _fileAccessOptions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ChatFileAccessService> _logger;

    private static readonly Regex ValidRefRegex = ValidRefPattern();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    // Extension-to-language map (mirrors GitService)
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".csx"] = "csharp",
        [".ts"] = "typescript", [".tsx"] = "typescript",
        [".js"] = "javascript", [".jsx"] = "javascript", [".mjs"] = "javascript",
        [".py"] = "python", [".pyi"] = "python",
        [".go"] = "go", [".java"] = "java", [".rs"] = "rust",
        [".rb"] = "ruby", [".php"] = "php", [".swift"] = "swift",
        [".kt"] = "kotlin", [".kts"] = "kotlin", [".scala"] = "scala",
        [".c"] = "c", [".h"] = "c",
        [".cpp"] = "cpp", [".cc"] = "cpp", [".cxx"] = "cpp", [".hpp"] = "cpp",
        [".html"] = "html", [".htm"] = "html",
        [".css"] = "css", [".scss"] = "scss", [".less"] = "less",
        [".json"] = "json", [".xml"] = "xml", [".csproj"] = "xml", [".sln"] = "xml",
        [".yaml"] = "yaml", [".yml"] = "yaml",
        [".md"] = "markdown", [".sql"] = "sql",
        [".sh"] = "shell", [".bash"] = "shell", [".zsh"] = "shell",
        [".ps1"] = "powershell", [".dockerfile"] = "dockerfile",
        [".proto"] = "protobuf", [".toml"] = "toml", [".lua"] = "lua",
    };

    // Known binary extensions
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin",
        ".zip", ".tar", ".gz", ".jar",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".tiff",
        ".mp3", ".mp4", ".wav",
        ".woff", ".woff2", ".ttf", ".eot",
        ".pyc", ".class", ".o", ".obj", ".pdb"
    };

    public ChatFileAccessService(
        CodeIndexDbContext context,
        IGitService gitService,
        IOptions<IndexingOptions> indexingOptions,
        IOptions<ChatFileAccessOptions> fileAccessOptions,
        IMemoryCache cache,
        ILogger<ChatFileAccessService> logger)
    {
        _context = context;
        _gitService = gitService;
        _indexingOptions = indexingOptions.Value;
        _fileAccessOptions = fileAccessOptions.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ChatFileContent> FetchFileForChatAsync(
        Guid repositoryId,
        string gitRef,
        string filePath,
        string? userId = null,
        CancellationToken ct = default)
    {
        // 1. Validate path
        if (!IsValidPath(filePath))
        {
            _logger.LogWarning("Rejected invalid file path: {FilePath} for user {UserId}", filePath, userId);
            return new ChatFileContent
            {
                FilePath = filePath,
                Error = "Invalid file path: path contains forbidden characters or traversal patterns"
            };
        }

        // 2. Validate ref
        if (!IsValidRef(gitRef))
        {
            _logger.LogWarning("Rejected invalid ref: {Ref} for user {UserId}", gitRef, userId);
            return new ChatFileContent
            {
                FilePath = filePath,
                Error = $"Invalid ref format: '{gitRef}'"
            };
        }

        // 3. Check binary by extension
        var ext = Path.GetExtension(filePath);
        if (BinaryExtensions.Contains(ext))
        {
            _logger.LogInformation("Binary file detected by extension: {FilePath} ref={Ref} user={UserId}",
                filePath, gitRef, userId);
            return new ChatFileContent
            {
                FilePath = filePath,
                IsBinary = true,
                Error = "Binary file, content not displayed"
            };
        }

        // 4. Verify repository exists
        var repo = await _context.Repositories.FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null)
        {
            return new ChatFileContent
            {
                FilePath = filePath,
                Error = $"Repository not found: {repositoryId}"
            };
        }

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repositoryId);
        if (!Directory.Exists(cloneDir))
        {
            return new ChatFileContent
            {
                FilePath = filePath,
                Error = "Repository not cloned yet"
            };
        }

        // 5. Resolve ref to SHA
        var resolvedSha = await _gitService.ResolveRefAsync(cloneDir, gitRef, ct);
        if (resolvedSha is null)
        {
            return new ChatFileContent
            {
                FilePath = filePath,
                Error = $"Ref not found: '{gitRef}'"
            };
        }

        // 6. Check cache (by SHA + path for immutability)
        var cacheKey = $"chat_file:{resolvedSha}:{filePath}";
        if (_cache.TryGetValue(cacheKey, out ChatFileContent? cached) && cached is not null)
        {
            _logger.LogInformation(
                "Cache hit: repo={RepoId} ref={Ref} sha={Sha} path={FilePath} user={UserId}",
                repositoryId, gitRef, resolvedSha, filePath, userId);
            return cached;
        }

        // 7. Read file content
        var content = await _gitService.ReadFileAsync(cloneDir, resolvedSha, filePath, ct);
        if (content is null)
        {
            var result = new ChatFileContent
            {
                FilePath = filePath,
                ResolvedSha = resolvedSha,
                Error = "File not found"
            };
            return result;
        }

        // 8. Check size
        var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(content);
        if (sizeBytes > _fileAccessOptions.MaxFileSizeBytes)
        {
            var sizeKb = sizeBytes / 1024;
            var maxKb = _fileAccessOptions.MaxFileSizeBytes / 1024;
            _logger.LogInformation(
                "File too large: repo={RepoId} path={FilePath} size={SizeKB}KB max={MaxKB}KB user={UserId}",
                repositoryId, filePath, sizeKb, maxKb, userId);
            return new ChatFileContent
            {
                FilePath = filePath,
                ResolvedSha = resolvedSha,
                Size = sizeBytes,
                Error = $"File too large ({sizeKb}KB, max {maxKb}KB)"
            };
        }

        // 9. Detect binary content (heuristic: null bytes in first 8KB)
        if (IsBinaryContent(content))
        {
            _logger.LogInformation("Binary content detected: repo={RepoId} path={FilePath} user={UserId}",
                repositoryId, filePath, userId);
            return new ChatFileContent
            {
                FilePath = filePath,
                ResolvedSha = resolvedSha,
                Size = sizeBytes,
                IsBinary = true,
                Error = "Binary file, content not displayed"
            };
        }

        // 10. Determine language
        var language = ext.Length > 0 && LanguageMap.TryGetValue(ext, out var lang) ? lang : null;

        var fileContent = new ChatFileContent
        {
            Content = content,
            FilePath = filePath,
            ResolvedSha = resolvedSha,
            Size = sizeBytes,
            Language = language
        };

        // Cache by SHA+path (immutable)
        _cache.Set(cacheKey, fileContent, CacheDuration);

        _logger.LogInformation(
            "File fetched: repo={RepoId} ref={Ref} sha={Sha} path={FilePath} size={Size} cached=false user={UserId}",
            repositoryId, gitRef, resolvedSha, filePath, sizeBytes, userId);

        return fileContent;
    }

    internal static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Reject null bytes
        if (path.Contains('\0')) return false;

        // Reject control characters
        foreach (var c in path)
        {
            if (char.IsControl(c)) return false;
        }

        // Reject path traversal
        if (path.Contains("..")) return false;

        return true;
    }

    internal static bool IsValidRef(string gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef)) return false;
        return ValidRefRegex.IsMatch(gitRef);
    }

    private static bool IsBinaryContent(string content)
    {
        // Check first 8KB for null bytes
        var checkLength = Math.Min(content.Length, 8192);
        for (var i = 0; i < checkLength; i++)
        {
            if (content[i] == '\0') return true;
        }
        return false;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9._/\-]+$")]
    private static partial Regex ValidRefPattern();
}
