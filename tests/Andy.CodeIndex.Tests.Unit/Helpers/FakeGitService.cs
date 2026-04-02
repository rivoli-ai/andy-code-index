using System.Text;
using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;

namespace Andy.CodeIndex.Tests.Unit.Helpers;

/// <summary>
/// A deterministic in-memory IGitService implementation for integration testing.
///
/// Simulates a 5-commit git history:
///   Commit 1 (aaa1111): Add README.md + src/main.py
///   Commit 2 (bbb2222): Modify src/main.py (README unchanged)
///   Commit 3 (ccc3333): Add src/utils.py (others unchanged)
///   Commit 4 (ddd4444): Delete README.md, modify src/main.py
///   Commit 5 (eee5555): Empty commit (no file changes vs commit 4)
/// </summary>
public class FakeGitService : IGitService
{
    public const string Commit1Sha = "aaaa111111aaaa111111aaaa111111aaaa111111"; // 40 chars
    public const string Commit2Sha = "bbbb222222bbbb222222bbbb222222bbbb222222"; // 40 chars
    public const string Commit3Sha = "cccc333333cccc333333cccc333333cccc333333"; // 40 chars
    public const string Commit4Sha = "dddd444444dddd444444dddd444444dddd444444"; // 40 chars
    public const string Commit5Sha = "eeee555555eeee555555eeee555555eeee555555"; // 40 chars

    private static readonly DateTime BaseDate = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // Blob hashes for each file version
    private const string BlobReadmeV1 = "blob-readme-v1";
    private const string BlobMainV1 = "blob-main-v1";
    private const string BlobMainV2 = "blob-main-v2";
    private const string BlobMainV3 = "blob-main-v3";
    private const string BlobUtilsV1 = "blob-utils-v1";

    // Tree hashes per commit
    private const string TreeHash1 = "tree-hash-commit-1";
    private const string TreeHash2 = "tree-hash-commit-2";
    private const string TreeHash3 = "tree-hash-commit-3";
    private const string TreeHash4 = "tree-hash-commit-4";
    private const string TreeHash5 = "tree-hash-commit-5";

    /// <summary>
    /// The ordered list of commits (newest first, like git log).
    /// </summary>
    private static readonly List<GitCommitInfo> AllCommits =
    [
        new()
        {
            Sha = Commit5Sha, Message = "Empty commit", AuthorName = "Test Dev",
            AuthorEmail = "dev@test.com", CommittedAt = BaseDate.AddHours(4),
            ParentShas = [Commit4Sha]
        },
        new()
        {
            Sha = Commit4Sha, Message = "Delete README, modify main.py", AuthorName = "Test Dev",
            AuthorEmail = "dev@test.com", CommittedAt = BaseDate.AddHours(3),
            ParentShas = [Commit3Sha]
        },
        new()
        {
            Sha = Commit3Sha, Message = "Add utils.py", AuthorName = "Test Dev",
            AuthorEmail = "dev@test.com", CommittedAt = BaseDate.AddHours(2),
            ParentShas = [Commit2Sha]
        },
        new()
        {
            Sha = Commit2Sha, Message = "Modify main.py", AuthorName = "Test Dev",
            AuthorEmail = "dev@test.com", CommittedAt = BaseDate.AddHours(1),
            ParentShas = [Commit1Sha]
        },
        new()
        {
            Sha = Commit1Sha, Message = "Initial commit", AuthorName = "Test Dev",
            AuthorEmail = "dev@test.com", CommittedAt = BaseDate,
            ParentShas = []
        }
    ];

    /// <summary>
    /// File listing per commit SHA. Each file has (path, size, language, blobHash).
    /// </summary>
    private static readonly Dictionary<string, List<(string Path, long Size, string? Language, string Hash)>> FilesPerCommit = new()
    {
        [Commit1Sha] =
        [
            ("README.md", 120, "markdown", BlobReadmeV1),
            ("src/main.py", 250, "python", BlobMainV1)
        ],
        [Commit2Sha] =
        [
            ("README.md", 120, "markdown", BlobReadmeV1), // unchanged
            ("src/main.py", 300, "python", BlobMainV2) // modified
        ],
        [Commit3Sha] =
        [
            ("README.md", 120, "markdown", BlobReadmeV1), // unchanged
            ("src/main.py", 300, "python", BlobMainV2), // unchanged from commit 2
            ("src/utils.py", 180, "python", BlobUtilsV1) // new file
        ],
        [Commit4Sha] =
        [
            // README.md deleted
            ("src/main.py", 350, "python", BlobMainV3), // modified
            ("src/utils.py", 180, "python", BlobUtilsV1) // unchanged
        ],
        [Commit5Sha] =
        [
            // Same as commit 4 — empty commit
            ("src/main.py", 350, "python", BlobMainV3),
            ("src/utils.py", 180, "python", BlobUtilsV1)
        ]
    };

    /// <summary>
    /// File content per (commitSha, filePath).
    /// </summary>
    private static readonly Dictionary<(string Sha, string Path), string> FileContents = new()
    {
        [(Commit1Sha, "README.md")] = "# Test Project\n\nThis is a test project for integration testing.\n\nIt contains sample Python code.",
        [(Commit1Sha, "src/main.py")] = "def main():\n    print('Hello, World!')\n\ndef helper():\n    return 42\n\nif __name__ == '__main__':\n    main()",
        [(Commit2Sha, "README.md")] = "# Test Project\n\nThis is a test project for integration testing.\n\nIt contains sample Python code.",
        [(Commit2Sha, "src/main.py")] = "def main():\n    print('Hello, World!')\n    print('Version 2')\n\ndef helper():\n    return 42\n\ndef new_function():\n    return 'added in commit 2'\n\nif __name__ == '__main__':\n    main()",
        [(Commit3Sha, "README.md")] = "# Test Project\n\nThis is a test project for integration testing.\n\nIt contains sample Python code.",
        [(Commit3Sha, "src/main.py")] = "def main():\n    print('Hello, World!')\n    print('Version 2')\n\ndef helper():\n    return 42\n\ndef new_function():\n    return 'added in commit 2'\n\nif __name__ == '__main__':\n    main()",
        [(Commit3Sha, "src/utils.py")] = "def utility_a():\n    return 'utility A'\n\ndef utility_b():\n    return 'utility B'\n\ndef shared_helper(x):\n    return x * 2",
        [(Commit4Sha, "src/main.py")] = "import utils\n\ndef main():\n    print('Hello, World!')\n    print('Version 3')\n    utils.utility_a()\n\ndef helper():\n    return 42\n\ndef new_function():\n    return 'modified in commit 4'\n\nif __name__ == '__main__':\n    main()",
        [(Commit4Sha, "src/utils.py")] = "def utility_a():\n    return 'utility A'\n\ndef utility_b():\n    return 'utility B'\n\ndef shared_helper(x):\n    return x * 2",
        [(Commit5Sha, "src/main.py")] = "import utils\n\ndef main():\n    print('Hello, World!')\n    print('Version 3')\n    utils.utility_a()\n\ndef helper():\n    return 42\n\ndef new_function():\n    return 'modified in commit 4'\n\nif __name__ == '__main__':\n    main()",
        [(Commit5Sha, "src/utils.py")] = "def utility_a():\n    return 'utility A'\n\ndef utility_b():\n    return 'utility B'\n\ndef shared_helper(x):\n    return x * 2"
    };

    private static readonly Dictionary<string, string> TreeHashes = new()
    {
        [Commit1Sha] = TreeHash1,
        [Commit2Sha] = TreeHash2,
        [Commit3Sha] = TreeHash3,
        [Commit4Sha] = TreeHash4,
        [Commit5Sha] = TreeHash5
    };

    public int CloneCallCount { get; private set; }
    public int FetchCallCount { get; private set; }

    public Task<string> CloneAsync(string url, string targetDir, string? pat = null, CancellationToken ct = default)
    {
        CloneCallCount++;
        return Task.FromResult(targetDir);
    }

    public Task FetchAsync(string repoDir, string? pat = null, CancellationToken ct = default)
    {
        FetchCallCount++;
        return Task.CompletedTask;
    }

    public Task<List<GitCommitInfo>> GetCommitsAsync(string repoDir, int limit = 100, string? sinceSha = null, CancellationToken ct = default)
    {
        var commits = AllCommits.ToList();

        if (sinceSha is not null)
        {
            // Return only commits newer than sinceSha
            var idx = commits.FindIndex(c => c.Sha == sinceSha);
            if (idx >= 0)
                commits = commits.Take(idx).ToList();
        }

        return Task.FromResult(commits.Take(limit).ToList());
    }

    public Task<List<GitCommitInfo>> GetCommitsAsync(string repoDir, string gitRef, int limit = 50, string? beforeSha = null, CancellationToken ct = default)
    {
        // For simplicity, treat any ref as "main" and return all commits
        return GetCommitsAsync(repoDir, limit, beforeSha, ct);
    }

    public Task<List<GitBranchInfo>> GetBranchesAsync(string repoDir, CancellationToken ct = default)
    {
        return Task.FromResult(new List<GitBranchInfo>
        {
            new() { Name = "main", HeadCommitSha = Commit5Sha, IsDefault = true }
        });
    }

    public Task<List<GitTagInfo>> GetTagsAsync(string repoDir, CancellationToken ct = default)
    {
        return Task.FromResult(new List<GitTagInfo>
        {
            new() { Name = "v1.0", CommitSha = Commit1Sha }
        });
    }

    public Task<string?> ReadFileAsync(string repoDir, string commitSha, string filePath, CancellationToken ct = default)
    {
        // Allow "HEAD" to resolve to latest commit
        var sha = commitSha == "HEAD" ? Commit5Sha : commitSha;

        return FileContents.TryGetValue((sha, filePath), out var content)
            ? Task.FromResult<string?>(content)
            : Task.FromResult<string?>(null);
    }

    public Task<byte[]?> ReadFileBytesAsync(string repoDir, string commitSha, string filePath, CancellationToken ct = default)
    {
        var sha = commitSha == "HEAD" ? Commit5Sha : commitSha;

        return FileContents.TryGetValue((sha, filePath), out var content)
            ? Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(content))
            : Task.FromResult<byte[]?>(null);
    }

    public Task<List<GitFileInfo>> ListFilesAsync(string repoDir, string commitSha, string? globPattern = null, CancellationToken ct = default)
    {
        var sha = commitSha == "HEAD" ? Commit5Sha : commitSha;

        if (!FilesPerCommit.TryGetValue(sha, out var fileList))
            return Task.FromResult(new List<GitFileInfo>());

        var files = fileList
            .Where(f => globPattern is null || MatchGlob(f.Path, globPattern))
            .Select(f => new GitFileInfo
            {
                Path = f.Path,
                Size = f.Size,
                Language = f.Language,
                Hash = f.Hash
            })
            .ToList();

        return Task.FromResult(files);
    }

    public Task<List<GitTreeEntry>> ListTreeAsync(string repoDir, string commitSha, string? path = null, bool recursive = false, CancellationToken ct = default)
    {
        var sha = commitSha == "HEAD" ? Commit5Sha : commitSha;

        if (!FilesPerCommit.TryGetValue(sha, out var fileList))
            return Task.FromResult(new List<GitTreeEntry>());

        var entries = fileList
            .Where(f => path is null || f.Path.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal))
            .Select(f => new GitTreeEntry
            {
                Path = f.Path,
                Name = f.Path.Contains('/') ? f.Path[(f.Path.LastIndexOf('/') + 1)..] : f.Path,
                Type = "blob",
                Hash = f.Hash,
                Size = f.Size,
                Language = f.Language
            })
            .ToList();

        return Task.FromResult(entries);
    }

    public Task<List<GrepResult>> GrepAsync(string repoDir, string pattern, string? globFilter = null, int limit = 50, CancellationToken ct = default)
    {
        // Search file contents at HEAD for the pattern
        var results = new List<GrepResult>();
        var headFiles = FilesPerCommit[Commit5Sha];

        foreach (var file in headFiles)
        {
            if (globFilter is not null && !MatchGlob(file.Path, globFilter))
                continue;

            if (!FileContents.TryGetValue((Commit5Sha, file.Path), out var content))
                continue;

            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length && results.Count < limit; i++)
            {
                if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase))
                {
                    results.Add(new GrepResult
                    {
                        FilePath = file.Path,
                        LineNumber = i + 1,
                        LineContent = lines[i]
                    });
                }
            }
        }

        return Task.FromResult(results);
    }

    public Task<string?> ResolveRefAsync(string repoDir, string gitRef, CancellationToken ct = default)
    {
        // Resolve "main" or "HEAD" to latest commit
        return gitRef switch
        {
            "main" or "HEAD" or "origin/main" => Task.FromResult<string?>(Commit5Sha),
            _ when AllCommits.Any(c => c.Sha == gitRef) => Task.FromResult<string?>(gitRef),
            _ => Task.FromResult<string?>(null)
        };
    }

    public Task<string?> GetTreeHashAsync(string repoDir, string gitRef, CancellationToken ct = default)
    {
        // Resolve ref to sha first
        var sha = gitRef switch
        {
            "main" or "HEAD" or "origin/main" => Commit5Sha,
            _ => gitRef
        };

        return TreeHashes.TryGetValue(sha, out var hash)
            ? Task.FromResult<string?>(hash)
            : Task.FromResult<string?>(null);
    }

    public Task<string> GetHeadRefAsync(string repoDir, CancellationToken ct = default)
    {
        return Task.FromResult(Commit5Sha);
    }

    public string GetCloneDir(string dataDir, Guid repositoryId)
        => Path.Combine(dataDir, "repos", repositoryId.ToString());

    /// <summary>
    /// Returns the list of all commits from newest to oldest.
    /// Useful for test assertions.
    /// </summary>
    public static List<GitCommitInfo> GetAllCommits() => AllCommits.ToList();

    /// <summary>
    /// Returns file info for a specific commit. Useful for test assertions.
    /// </summary>
    public static List<(string Path, long Size, string? Language, string Hash)>? GetFilesForCommit(string sha)
        => FilesPerCommit.TryGetValue(sha, out var files) ? files : null;

    internal static bool MatchGlob(string path, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", ".") + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }
}
