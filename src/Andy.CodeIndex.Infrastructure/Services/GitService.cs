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
        [".ts"] = "typescript", [".tsx"] = "typescript",
        [".js"] = "javascript", [".jsx"] = "javascript", [".mjs"] = "javascript",
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

    public async Task<List<GitCommitInfo>> GetCommitsAsync(string repoDir, int limit = 100, string? sinceSha = null, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--all", $"--max-count={limit}", "--format=%H%n%s%n%an%n%ae%n%aI%n---" };

        if (sinceSha is not null)
            args.Add($"{sinceSha}..HEAD");

        var output = await RunGitAsync(repoDir, args, ct);
        var commits = new List<GitCommitInfo>();

        var entries = output.Split("---\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var lines = entry.Split('\n', StringSplitOptions.None);
            if (lines.Length < 5) continue;

            commits.Add(new GitCommitInfo
            {
                Sha = lines[0].Trim(),
                Message = lines[1].Trim(),
                AuthorName = lines[2].Trim(),
                AuthorEmail = lines[3].Trim(),
                CommittedAt = DateTimeOffset.Parse(lines[4].Trim(), CultureInfo.InvariantCulture).UtcDateTime
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
            var ext = Path.GetExtension(path);

            files.Add(new GitFileInfo
            {
                Path = path,
                Size = size,
                Language = ext.Length > 0 && LanguageMap.TryGetValue(ext, out var lang) ? lang : null,
                Hash = meta[2]
            });
        }

        return files;
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

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("git {Args} exited with {Code}: {Stderr}",
                string.Join(' ', argList), process.ExitCode, stderr.Trim());
            throw new InvalidOperationException(
                $"git {argList[0]} failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout;
    }
}
