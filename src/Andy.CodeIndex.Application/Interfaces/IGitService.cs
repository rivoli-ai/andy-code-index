using Andy.CodeIndex.Domain.Entities;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IGitService
{
    Task<string> CloneAsync(string url, string targetDir, string? pat = null, CancellationToken ct = default);
    Task FetchAsync(string repoDir, string? pat = null, CancellationToken ct = default);
    Task<List<GitCommitInfo>> GetCommitsAsync(string repoDir, int limit = 100, string? sinceSha = null, CancellationToken ct = default);
    Task<List<GitBranchInfo>> GetBranchesAsync(string repoDir, CancellationToken ct = default);
    Task<List<GitTagInfo>> GetTagsAsync(string repoDir, CancellationToken ct = default);
    Task<string?> ReadFileAsync(string repoDir, string commitSha, string filePath, CancellationToken ct = default);
    Task<List<GitFileInfo>> ListFilesAsync(string repoDir, string commitSha, string? globPattern = null, CancellationToken ct = default);
    Task<List<GrepResult>> GrepAsync(string repoDir, string pattern, string? globFilter = null, int limit = 50, CancellationToken ct = default);
    Task<string?> ResolveRefAsync(string repoDir, string gitRef, CancellationToken ct = default);
    Task<string?> GetTreeHashAsync(string repoDir, string gitRef, CancellationToken ct = default);
    string GetCloneDir(string dataDir, Guid repositoryId);
}

public class GitCommitInfo
{
    public required string Sha { get; set; }
    public required string Message { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public DateTime CommittedAt { get; set; }
}

public class GitBranchInfo
{
    public required string Name { get; set; }
    public required string HeadCommitSha { get; set; }
    public bool IsDefault { get; set; }
}

public class GitTagInfo
{
    public required string Name { get; set; }
    public required string CommitSha { get; set; }
}

public class GitFileInfo
{
    public required string Path { get; set; }
    public long Size { get; set; }
    public string? Language { get; set; }
    public string? Hash { get; set; }
}

public class GrepResult
{
    public required string FilePath { get; set; }
    public int LineNumber { get; set; }
    public required string LineContent { get; set; }
}
