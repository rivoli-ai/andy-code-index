namespace Andy.CodeIndex.Application.DTOs;

// --- Git Log ---

public class GitLogResponseDto
{
    public List<GitLogCommitDto> Commits { get; set; } = new();
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}

public class GitLogCommitDto
{
    public required string Sha { get; set; }
    public required string AbbreviatedSha { get; set; }
    public required string Message { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public DateTime CommittedAt { get; set; }
    public List<string> ParentShas { get; set; } = new();
    public bool IsIndexed { get; set; }
    public int EnrichmentCount { get; set; }
}

// --- Git Refs ---

public class GitRefsResponseDto
{
    public List<GitRefBranchDto> Branches { get; set; } = new();
    public List<GitRefTagDto> Tags { get; set; } = new();
    public required string Head { get; set; }
}

public class GitRefBranchDto
{
    public required string Name { get; set; }
    public required string Sha { get; set; }
    public bool IsDefault { get; set; }
}

public class GitRefTagDto
{
    public required string Name { get; set; }
    public required string Sha { get; set; }
}

// --- Git Tree ---

public class GitTreeResponseDto
{
    public List<GitTreeEntryDto> Entries { get; set; } = new();
    public required string Ref { get; set; }
    public string? Path { get; set; }
    public bool Recursive { get; set; }
}

public class GitTreeEntryDto
{
    public required string Path { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Hash { get; set; }
    public long Size { get; set; }
    public string? Language { get; set; }
    public bool HasEnrichments { get; set; }
}

// --- Commit Summary ---

public class CommitSummaryResponseDto
{
    public required string Sha { get; set; }
    public bool IsIndexed { get; set; }
    public int TotalEnrichments { get; set; }
    public int FilesIndexed { get; set; }
    public int TotalFiles { get; set; }
    public int EmbeddingsCount { get; set; }
    public Dictionary<string, int> CountsBySubtype { get; set; } = new();
}
