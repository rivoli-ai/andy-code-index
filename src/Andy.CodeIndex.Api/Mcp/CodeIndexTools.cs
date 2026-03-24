using System.ComponentModel;
using System.Reflection;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Enums;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Api.Mcp;

[McpServerToolType]
public class CodeIndexTools
{
    private readonly IRepositoryService _repoService;
    private readonly ISearchService _searchService;
    private readonly IEnrichmentGeneratorService _enrichmentService;
    private readonly IGitService _gitService;
    private readonly IChatService _chatService;
    private readonly IndexingOptions _options;

    public CodeIndexTools(
        IRepositoryService repoService,
        ISearchService searchService,
        IEnrichmentGeneratorService enrichmentService,
        IGitService gitService,
        IChatService chatService,
        IOptions<IndexingOptions> options)
    {
        _repoService = repoService;
        _searchService = searchService;
        _enrichmentService = enrichmentService;
        _gitService = gitService;
        _chatService = chatService;
        _options = options.Value;
    }

    [McpServerTool(Name = "code_index_version"), Description("Get the Andy.CodeIndex server version")]
    public string GetVersion()
    {
        return Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";
    }

    [McpServerTool(Name = "code_index_repositories"), Description("List all repositories tracked by the code index")]
    public async Task<object> ListRepositories()
    {
        var repos = await _repoService.ListAsync();
        return repos.Select(r => new
        {
            r.Id, r.Name, r.Url, provider = r.Provider.ToString(),
            r.DefaultBranch, r.LastIndexedCommitSha, r.LastSyncedAt, r.Status
        });
    }

    [McpServerTool(Name = "code_index_architecture_docs"), Description("Get high-level architecture documentation for a repository")]
    public async Task<object> GetArchitectureDocs(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Physical, "Architecture documentation");
    }

    [McpServerTool(Name = "code_index_api_docs"), Description("Get API documentation for a repository")]
    public async Task<object> GetApiDocs(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.APIDocs, "API documentation");
    }

    [McpServerTool(Name = "code_index_commit_description"), Description("Get AI-generated commit description and context")]
    public async Task<object> GetCommitDescription(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.CommitDescription, "Commit description");
    }

    [McpServerTool(Name = "code_index_database_schema"), Description("Get database schema documentation for a repository")]
    public async Task<object> GetDatabaseSchema(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.DatabaseSchema, "Database schema");
    }

    [McpServerTool(Name = "code_index_cookbook"), Description("Get usage examples and cookbook for a repository")]
    public async Task<object> GetCookbook(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Cookbook, "Cookbook");
    }

    [McpServerTool(Name = "code_index_wiki"), Description("Get wiki table of contents for a repository")]
    public async Task<object> GetWiki(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Wiki, "Wiki");
    }

    [McpServerTool(Name = "code_index_wiki_page"), Description("Get a specific wiki page by slug")]
    public async Task<object> GetWikiPage(
        [Description("Repository URL or name")] string repo_url,
        [Description("Wiki page slug")] string page_slug,
        [Description("Commit SHA (defaults to latest)")] string? commit_sha = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var results = await _enrichmentService.QueryAsync(
            subtype: EnrichmentSubtype.Wiki,
            repositoryId: repo.Id,
            filePath: page_slug,
            limit: 1);

        return results.Count > 0
            ? new { page_slug, content = results[0].Content, title = results[0].Title }
            : new { error = $"Wiki page '{page_slug}' not found" } as object;
    }

    [McpServerTool(Name = "code_index_semantic_search"), Description("Search code using semantic similarity")]
    public async Task<object> SemanticSearch(
        [Description("Natural language search query")] string query,
        [Description("Programming language filter")] string? language = null,
        [Description("Repository URL to search within")] string? source_repo = null,
        [Description("Maximum results to return")] int? limit = null)
    {
        var filter = await BuildSearchFilter(language, source_repo);
        var results = await _searchService.SemanticSearchAsync(query, filter, limit ?? 10);
        return FormatSearchResults(results);
    }

    [McpServerTool(Name = "code_index_keyword_search"), Description("Search code using BM25 keyword matching")]
    public async Task<object> KeywordSearch(
        [Description("Keywords to search for")] string keywords,
        [Description("Repository URL to search within")] string? source_repo = null,
        [Description("Programming language filter")] string? language = null,
        [Description("Maximum results to return")] int? limit = null)
    {
        var filter = await BuildSearchFilter(language, source_repo);
        var results = await _searchService.KeywordSearchAsync(keywords, filter, limit ?? 10);
        return FormatSearchResults(results);
    }

    [McpServerTool(Name = "code_index_grep"), Description("Search file contents with regex pattern")]
    public async Task<object> Grep(
        [Description("Repository URL or name")] string repo_url,
        [Description("Regex pattern to search for")] string pattern,
        [Description("File glob filter (e.g., *.cs)")] string? glob = null,
        [Description("Maximum results to return")] int? limit = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        if (!Directory.Exists(cloneDir))
            return new { error = "Repository not cloned yet" };

        var results = await _gitService.GrepAsync(cloneDir, pattern, glob, limit ?? 50);
        return new { total = results.Count, matches = results };
    }

    [McpServerTool(Name = "code_index_read_resource"), Description("Read file content from a resource URI")]
    public async Task<object> ReadResource(
        [Description("Resource URI (code-index://repo-name/commit-sha/path)")] string uri)
    {
        if (!uri.StartsWith("code-index://"))
            return new { error = "Invalid URI format. Expected: code-index://repo-name/commit-sha/path" };

        var path = uri["code-index://".Length..];
        var parts = path.Split('/', 3);
        if (parts.Length < 3)
            return new { error = "Invalid URI format. Expected: code-index://repo-name/commit-sha/path" };

        var repoName = parts[0];
        var commitSha = parts[1];
        var filePath = parts[2];

        var repo = await ResolveRepo(repoName);
        if (repo is null)
            return new { error = $"Repository '{repoName}' not found" };

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        if (!Directory.Exists(cloneDir))
            return new { error = "Repository not cloned yet" };

        var content = await _gitService.ReadFileAsync(cloneDir, commitSha, filePath);
        if (content is null)
            return new { error = $"File '{filePath}' not found at commit {commitSha}" };

        return new { path = filePath, commitSha, content, lineCount = content.Split('\n').Length };
    }

    [McpServerTool(Name = "code_index_ls"), Description("List files matching a glob pattern in a repository")]
    public async Task<object> ListFiles(
        [Description("Repository URL or name")] string repo_url,
        [Description("Glob pattern to match files (e.g., **/*.cs)")] string pattern)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        if (!Directory.Exists(cloneDir))
            return new { error = "Repository not cloned yet" };

        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, pattern);
        return new { total = files.Count, files = files.Select(f => new { f.Path, f.Size, f.Language }) };
    }

    [McpServerTool(Name = "code_index_chat"), Description("Chat with the indexed codebase - ask questions about code structure, patterns, complexity")]
    public async Task<object> Chat(
        [Description("Your question about the codebase")] string message,
        [Description("Repository name to scope the conversation (optional)")] string? repository = null,
        [Description("Conversation ID for follow-up messages")] string? conversation_id = null)
    {
        Guid? repoId = null;
        if (repository is not null)
        {
            var repo = await ResolveRepo(repository);
            if (repo is not null) repoId = repo.Id;
        }

        var response = await _chatService.ChatAsync(new ChatRequest
        {
            Message = message,
            RepositoryId = repoId,
            ConversationId = conversation_id
        });

        return new
        {
            reply = response.Reply,
            conversationId = response.ConversationId,
            model = response.Model,
            sources = response.Sources.Select(s => new
            {
                s.FilePath, s.RepositoryName, s.StartLine, s.EndLine, s.Language
            })
        };
    }

    [McpServerTool(Name = "code_index_commit_history"), Description("Get full commit log and tags for a repository")]
    public async Task<object> GetCommitHistory(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.CommitHistory, "Commit history");
    }

    [McpServerTool(Name = "code_index_dependencies"), Description("Get package dependencies for a repository")]
    public async Task<object> GetDependencies(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Dependencies, "Dependencies");
    }

    [McpServerTool(Name = "code_index_analytics"), Description("Get repository analytics: languages, file types, top terms, complex files")]
    public async Task<object> GetAnalytics(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null) return new { error = $"Repository '{repo_url}' not found" };

        var details = await _repoService.GetDetailsByIdAsync(repo.Id);
        return new
        {
            repository = repo.Name,
            status = repo.Status,
            stats = details?.Stats,
            branches = details?.Branches?.Select(b => b.Name),
            hint = "Use code_index_semantic_search or code_index_keyword_search to find specific code"
        };
    }

    [McpServerTool(Name = "code_index_sync_status"), Description("Get periodic sync schedule and repository count")]
    public async Task<object> GetSyncStatus()
    {
        var repos = await _repoService.ListAsync();
        return new
        {
            repositoryCount = repos.Count,
            indexed = repos.Count(r => r.Status == "indexed"),
            repositories = repos.Select(r => new { r.Name, r.Status, r.LastSyncedAt,
                enrichments = r.Stats?.EnrichmentCount ?? 0,
                embeddings = r.Stats?.EmbeddingCount ?? 0,
                hasEmbeddings = r.Stats?.HasEmbeddings ?? false })
        };
    }

    // --- Helpers ---

    private async Task<RepositoryDto?> ResolveRepo(string urlOrName)
    {
        // Try by URL first, then by name
        var repos = await _repoService.ListAsync();
        return repos.FirstOrDefault(r =>
            r.Url.Equals(urlOrName, StringComparison.OrdinalIgnoreCase) ||
            r.Name.Equals(urlOrName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<object> GetEnrichmentBySubtype(string repoUrlOrName, EnrichmentSubtype subtype, string label)
    {
        var repo = await ResolveRepo(repoUrlOrName);
        if (repo is null)
            return new { error = $"Repository '{repoUrlOrName}' not found" };

        var results = await _enrichmentService.QueryAsync(
            subtype: subtype,
            repositoryId: repo.Id,
            limit: 10);

        return results.Count > 0
            ? new { repository = repo.Name, type = label, results = results.Select(r => new { r.Title, r.Content, r.FilePath }) }
            : new { repository = repo.Name, message = $"No {label.ToLowerInvariant()} available. Index the repository first." } as object;
    }

    private async Task<SearchFilter> BuildSearchFilter(string? language, string? sourceRepo)
    {
        var filter = new SearchFilter();
        if (language is not null) filter.Languages = [language];
        if (sourceRepo is not null)
        {
            var repo = await ResolveRepo(sourceRepo);
            if (repo is not null) filter.RepositoryIds = [repo.Id];
        }
        return filter;
    }

    private static object FormatSearchResults(SearchResultsDto results)
    {
        return new
        {
            total = results.TotalCount,
            mode = results.SearchMode,
            duration_ms = results.DurationMs,
            truncated = results.Results.Count < results.TotalCount,
            results = results.Results.Select(r => new
            {
                r.FilePath,
                r.StartLine,
                r.EndLine,
                r.Language,
                r.RepositoryName,
                r.Score,
                content = r.Content.Length > 500 ? r.Content[..500] + "..." : r.Content,
                resource_uri = r.RepositoryName is not null
                    ? $"code-index://{r.RepositoryName}/{r.CommitSha ?? "HEAD"}/{r.FilePath}"
                    : null
            }),
            hints = new
            {
                read_file = "Use code_index_read_resource with the resource_uri to view the full file",
                more_results = results.Results.Count < results.TotalCount ? "Increase limit to see more results" : null,
                related = "Use code_index_api_docs or code_index_architecture_docs to understand the repository structure"
            }
        };
    }
}
