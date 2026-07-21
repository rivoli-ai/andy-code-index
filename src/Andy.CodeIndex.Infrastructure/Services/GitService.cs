using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Services;

public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;

    // Map file extensions to language names
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".csx"] = "csharp",
        [".ts"] = "typescript", [".tsx"] = "typescript", [".cts"] = "typescript", [".mts"] = "typescript",
        [".js"] = "javascript", [".jsx"] = "javascript", [".mjs"] = "javascript", [".cjs"] = "javascript",
        [".py"] = "python", [".pyi"] = "python",
        [".go"] = "go",
        [".java"] = "java",
        [".rs"] = "rust",
        [".rb"] = "ruby",
        [".php"] = "php",
        [".swift"] = "swift",
        [".kt"] = "kotlin", [".kts"] = "kotlin",
        [".scala"] = "scala",
        [".c"] = "c", [".h"] = "c",
        [".cpp"] = "cpp", [".cc"] = "cpp", [".cxx"] = "cpp", [".hpp"] = "cpp",
        [".html"] = "html", [".htm"] = "html",
        [".css"] = "css", [".scss"] = "scss", [".less"] = "less",
        [".json"] = "json",
        [".xml"] = "xml", [".csproj"] = "xml", [".sln"] = "xml",
        [".yaml"] = "yaml", [".yml"] = "yaml",
        [".md"] = "markdown",
        [".sql"] = "sql",
        [".sh"] = "shell", [".bash"] = "shell", [".zsh"] = "shell",
        [".ps1"] = "powershell",
        [".dockerfile"] = "dockerfile",
        [".proto"] = "protobuf",
        [".toml"] = "toml",
        [".lua"] = "lua",
        [".r"] = "r", [".R"] = "r",
        [".dart"] = "dart",
        [".ex"] = "elixir", [".exs"] = "elixir",
        [".erl"] = "erlang",
        [".zig"] = "zig",
        [".vue"] = "vue",
        [".svelte"] = "svelte",
    };

    /// <summary>Maps a file path to a language by extension, or null if unknown.</summary>
    internal static string? DetectLanguage(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Length > 0 && LanguageMap.TryGetValue(ext, out var lang) ? lang : null;
    }

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger;
    }

    public string GetCloneDir(string dataDir, Guid repositoryId)
        => Path.Combine(dataDir, "repos", repositoryId.ToString());

    public async Task<string> CloneAsync(string url, string targetDir, string? pat = null, CancellationToken ct = default)
    {
        if (Directory.Exists(targetDir))
        {
            _logger.LogInformation("Clone directory already exists, fetching instead: {Dir}", targetDir);
            await FetchAsync(targetDir, pat, ct);
            return targetDir;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

        var cloneUrl = pat is not null ? EmbedPat(url, pat) : url;
        await RunGitAsync(null, ["clone", "--no-checkout", cloneUrl, targetDir], ct);

        _logger.LogInformation("Cloned {Url} to {Dir}", url, targetDir);
        return targetDir;
    }

    public async Task FetchAsync(string repoDir, string? pat = null, CancellationToken ct = default)
    {
        if (pat is not null)
        {
            var url = await RunGitAsync(repoDir, ["remote", "get-url", "origin"], ct);
            var patUrl = EmbedPat(url.Trim(), pat);
            await RunGitAsync(repoDir, ["remote", "set-url", "origin", patUrl], ct);
        }

        await RunGitAsync(repoDir, ["fetch", "--all", "--prune"], ct);
        _logger.LogInformation("Fetched latest for {Dir}", repoDir);
    }

    public async Task<List<GitCommitInfo>> GetCommitsAsync(string repoDir, int limit = 10000, string? sinceSha = null, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--all", $"--max-count={limit}", "--format=%H%n%P%n%s%n%an%n%ae%n%aI%n---" };

        if (sinceSha is not null)
            args.Add($"{sinceSha}..HEAD");

        return await ParseCommitLogAsync(repoDir, args, ct);
    }

    public async Task<List<GitCommitInfo>> GetCommitsAsync(string repoDir, string gitRef, int limit = 50, string? beforeSha = null, CancellationToken ct = default)
    {
        if (!IsValidRef(gitRef))
            throw new ArgumentException($"Invalid git ref: {gitRef}");

        var args = new List<string> { "log", $"--max-count={limit}", "--format=%H%n%P%n%s%n%an%n%ae%n%aI%n---" };

        if (beforeSha is not null)
        {
            if (!IsValidRef(beforeSha))
                throw new ArgumentException($"Invalid before SHA: {beforeSha}");
            // Show commits reachable from ref, starting before the cursor SHA
            args.Add(gitRef);
            // Skip the cursor commit itself: use ^beforeSha~ to exclude it
            args.AddRange(["--not", $"{beforeSha}"]);
        }
        else
        {
            args.Add(gitRef);
        }

        return await ParseCommitLogAsync(repoDir, args, ct);
    }

    internal async Task<List<GitCommitInfo>> ParseCommitLogAsync(string repoDir, List<string> args, CancellationToken ct)
    {
        var output = await RunGitAsync(repoDir, args, ct);
        return ParseCommitLog(output);
    }

    internal static List<GitCommitInfo> ParseCommitLog(string output)
    {
        var commits = new List<GitCommitInfo>();
        var entries = output.Split("---\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var lines = entry.Split('\n', StringSplitOptions.None);
            if (lines.Length < 6) continue;

            var sha = lines[0].Trim();
            if (string.IsNullOrEmpty(sha)) continue;

            var parentLine = lines[1].Trim();
            var parentShas = string.IsNullOrEmpty(parentLine)
                ? new List<string>()
                : parentLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (!DateTimeOffset.TryParse(lines[5].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var committedAt))
                continue;

            commits.Add(new GitCommitInfo
            {
                Sha = sha,
                Message = lines[2].Trim(),
                AuthorName = lines[3].Trim(),
                AuthorEmail = lines[4].Trim(),
                CommittedAt = committedAt.UtcDateTime,
                ParentShas = parentShas
            });
        }

        return commits;
    }

    public async Task<List<GitBranchInfo>> GetBranchesAsync(string repoDir, CancellationToken ct = default)
    {
        var output = await RunGitAsync(repoDir, ["branch", "-r", "--format=%(refname:short) %(objectname:short)"], ct);
        var defaultBranch = await GetDefaultBranchAsync(repoDir, ct);

        var branches = new List<GitBranchInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 2);
            if (parts.Length < 2) continue;

            var name = parts[0];
            // Skip HEAD pointer
            if (name.Contains("HEAD", StringComparison.OrdinalIgnoreCase)) continue;
            // Remove origin/ prefix
            if (name.StartsWith("origin/", StringComparison.Ordinal))
                name = name[7..];

            branches.Add(new GitBranchInfo
            {
                Name = name,
                HeadCommitSha = parts[1],
                IsDefault = name == defaultBranch
            });
        }

        return branches;
    }

    public async Task<List<GitTagInfo>> GetTagsAsync(string repoDir, CancellationToken ct = default)
    {
        var output = await RunGitAsync(repoDir, ["tag", "--format=%(refname:short) %(objectname:short)"], ct);
        var tags = new List<GitTagInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 2);
            if (parts.Length < 2) continue;

            tags.Add(new GitTagInfo { Name = parts[0], CommitSha = parts[1] });
        }

        return tags;
    }

    public async Task<string?> ReadFileAsync(string repoDir, string commitSha, string filePath, CancellationToken ct = default)
    {
        try
        {
            return await RunGitAsync(repoDir, ["show", $"{commitSha}:{filePath}"], ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<byte[]?> ReadFileBytesAsync(string repoDir, string commitSha, string filePath, CancellationToken ct = default)
    {
        try
        {
            return await RunGitBinaryAsync(repoDir, ["show", $"{commitSha}:{filePath}"], ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<List<GitFileInfo>> ListFilesAsync(string repoDir, string commitSha, string? globPattern = null, CancellationToken ct = default)
    {
        var output = await RunGitAsync(repoDir, ["ls-tree", "-r", "--long", commitSha], ct);
        var files = new List<GitFileInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: mode type hash size\tpath
            var tabIndex = line.IndexOf('\t');
            if (tabIndex < 0) continue;

            var meta = line[..tabIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = line[(tabIndex + 1)..];

            if (meta.Length < 4) continue;

            // Apply glob filter
            if (globPattern is not null && !MatchGlob(path, globPattern))
                continue;

            // Skip .gitignore-excluded patterns
            if (ShouldSkipFile(path))
                continue;

            long.TryParse(meta[3], out var size);

            files.Add(new GitFileInfo
            {
                Path = path,
                Size = size,
                Language = DetectLanguage(path),
                Hash = meta[2]
            });
        }

        return files;
    }

    public async Task<List<GitTreeEntry>> ListTreeAsync(string repoDir, string commitSha, string? path = null, bool recursive = false, CancellationToken ct = default)
    {
        var args = new List<string> { "ls-tree", "--long" };
        if (recursive)
            args.Add("-r");

        if (!string.IsNullOrEmpty(path))
        {
            // Normalize path: ensure it ends with / for directory listing
            var normalizedPath = path.TrimEnd('/') + "/";
            args.Add($"{commitSha}:{normalizedPath}");
        }
        else
        {
            args.Add(commitSha);
        }

        string output;
        try
        {
            output = await RunGitAsync(repoDir, args, ct);
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        var entries = new List<GitTreeEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: mode type hash size\tpath
            var tabIndex = line.IndexOf('\t');
            if (tabIndex < 0) continue;

            var meta = line[..tabIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var entryPath = line[(tabIndex + 1)..];

            if (meta.Length < 4) continue;

            var type = meta[1]; // "blob" or "tree"
            long size = 0;
            if (type == "blob")
                long.TryParse(meta[3], out size);

            // For non-recursive listing, entryPath is just the name;
            // for recursive listing, it's the full relative path.
            var name = entryPath.Contains('/')
                ? entryPath[(entryPath.LastIndexOf('/') + 1)..]
                : entryPath;

            var fullPath = !string.IsNullOrEmpty(path)
                ? path.TrimEnd('/') + "/" + entryPath
                : entryPath;

            string? language = null;
            if (type == "blob")
                language = DetectLanguage(entryPath);

            entries.Add(new GitTreeEntry
            {
                Path = fullPath,
                Name = name,
                Type = type,
                Hash = meta[2],
                Size = size,
                Language = language
            });
        }

        return entries;
    }

    public async Task<string> GetHeadRefAsync(string repoDir, CancellationToken ct = default)
    {
        try
        {
            var output = await RunGitAsync(repoDir, ["rev-parse", "HEAD"], ct);
            return output.Trim();
        }
        catch (InvalidOperationException)
        {
            return "HEAD";
        }
    }

    public async Task<string?> ResolveRefAsync(string repoDir, string gitRef, CancellationToken ct = default)
    {
        if (!IsValidRef(gitRef)) return null;

        try
        {
            var output = await RunGitAsync(repoDir, ["rev-parse", gitRef], ct);
            var sha = output.Trim();
            return sha.Length > 0 ? sha : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<string?> GetTreeHashAsync(string repoDir, string gitRef, CancellationToken ct = default)
    {
        if (!IsValidRef(gitRef)) return null;

        try
        {
            var output = await RunGitAsync(repoDir, ["rev-parse", $"{gitRef}^{{tree}}"], ct);
            var hash = output.Trim();
            return hash.Length > 0 ? hash : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool IsValidRef(string gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef)) return false;
        return Regex.IsMatch(gitRef, @"^[a-zA-Z0-9._/\-~^{}]+$");
    }

    public async Task<List<GrepResult>> GrepAsync(string repoDir, string pattern, string? globFilter = null, int limit = 50, CancellationToken ct = default)
    {
        var args = new List<string> { "grep", "-n", "-I", "--no-color", pattern, "HEAD" };

        if (globFilter is not null)
            args.AddRange(["--", globFilter]);

        string output;
        try
        {
            output = await RunGitAsync(repoDir, args, ct);
        }
        catch (InvalidOperationException)
        {
            // grep returns exit code 1 when no matches found
            return [];
        }

        var results = new List<GrepResult>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (results.Count >= limit) break;

            // Format: HEAD:path:linenum:content
            var match = Regex.Match(line, @"^HEAD:(.+?):(\d+):(.*)$");
            if (!match.Success) continue;

            results.Add(new GrepResult
            {
                FilePath = match.Groups[1].Value,
                LineNumber = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                LineContent = match.Groups[3].Value
            });
        }

        return results;
    }

    private async Task<string> GetDefaultBranchAsync(string repoDir, CancellationToken ct)
    {
        try
        {
            var output = await RunGitAsync(repoDir, ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], ct);
            var branch = output.Trim();
            return branch.StartsWith("origin/", StringComparison.Ordinal) ? branch[7..] : branch;
        }
        catch
        {
            return "main";
        }
    }

    private static string EmbedPat(string url, string pat)
    {
        var uri = new Uri(url);
        return $"{uri.Scheme}://x-access-token:{pat}@{uri.Host}{uri.PathAndQuery}";
    }

    internal static bool MatchGlob(string path, string pattern)
    {
        // Simple glob: * matches any chars in a segment, ** matches any path
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", ".") + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }

    private static bool ShouldSkipFile(string path)
    {
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment is ".git" or "node_modules" or "bin" or "obj" or ".vs" or ".idea")
                return true;
        }
        return false;
    }

    private async Task<string> RunGitAsync(string? workingDir, IEnumerable<string> arguments, CancellationToken ct)
    {
        var argList = arguments.ToList();
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in argList)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        // Drain both redirected pipes concurrently. Reading either one to EOF
        // first can deadlock when Git fills the other OS pipe buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("git {Args} exited with {Code}: {Stderr}",
                string.Join(' ', argList), process.ExitCode, stderr.Trim());
            throw new InvalidOperationException(
                $"git {argList[0]} failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout;
    }

    private async Task<byte[]> RunGitBinaryAsync(string? workingDir, IEnumerable<string> arguments, CancellationToken ct)
    {
        var argList = arguments.ToList();
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in argList)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        using var ms = new MemoryStream();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }

        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("git {Args} exited with {Code}: {Stderr}",
                string.Join(' ', argList), process.ExitCode, stderr.Trim());
            throw new InvalidOperationException(
                $"git {argList[0]} failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return ms.ToArray();
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
        catch (NotSupportedException)
        {
            // Some platforms do not support killing an entire process tree.
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
        }
    }
}
