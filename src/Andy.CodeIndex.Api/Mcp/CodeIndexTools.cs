using System.ComponentModel;
using System.Reflection;
using Andy.CodeIndex.Api.Controllers;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using ModelContextProtocol.Server;
using Microsoft.EntityFrameworkCore;
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
    private readonly IChatFileAccessService _chatFileAccessService;
    private readonly ICommitRepository _commitRepo;
    private readonly IIndexingTaskRepository _taskRepo;
    private readonly IRepoDiscoveryService _discoveryService;
    private readonly IQuestionClassifier _questionClassifier;
    private readonly IReportService _reportService;
    private readonly IApiKeyResolver _apiKeyResolver;
    private readonly IEncryptionService _encryption;
    private readonly CodeIndexDbContext _dbContext;
    private readonly IndexingOptions _options;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly EnrichmentLlmOptions _llmOptions;

    public CodeIndexTools(
        IRepositoryService repoService,
        ISearchService searchService,
        IEnrichmentGeneratorService enrichmentService,
        IGitService gitService,
        IChatService chatService,
        IChatFileAccessService chatFileAccessService,
        ICommitRepository commitRepo,
        IIndexingTaskRepository taskRepo,
        IRepoDiscoveryService discoveryService,
        IQuestionClassifier questionClassifier,
        IReportService reportService,
        IApiKeyResolver apiKeyResolver,
        IEncryptionService encryption,
        CodeIndexDbContext dbContext,
        IOptions<IndexingOptions> options,
        IOptions<EmbeddingOptions> embeddingOptions,
        IOptions<EnrichmentLlmOptions> llmOptions)
    {
        _repoService = repoService;
        _searchService = searchService;
        _enrichmentService = enrichmentService;
        _gitService = gitService;
        _chatService = chatService;
        _chatFileAccessService = chatFileAccessService;
        _commitRepo = commitRepo;
        _taskRepo = taskRepo;
        _discoveryService = discoveryService;
        _questionClassifier = questionClassifier;
        _reportService = reportService;
        _apiKeyResolver = apiKeyResolver;
        _encryption = encryption;
        _dbContext = dbContext;
        _options = options.Value;
        _embeddingOptions = embeddingOptions.Value;
        _llmOptions = llmOptions.Value;
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

    [McpServerTool(Name = "code_index_chat"), Description("Chat with the indexed codebase - ask questions about code structure, patterns, complexity. Supports file access at specific git refs.")]
    public async Task<object> Chat(
        [Description("Your question about the codebase")] string message,
        [Description("Repository name to scope the conversation (optional)")] string? repository = null,
        [Description("Conversation ID for follow-up messages")] string? conversation_id = null,
        [Description("Git ref (branch, tag, or SHA) for file access context (optional, defaults to HEAD)")] string? git_ref = null)
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
            ConversationId = conversation_id,
            Ref = git_ref
        });

        return new
        {
            reply = response.Reply,
            conversationId = response.ConversationId,
            model = response.Model,
            sources = response.Sources.Select(s => new
            {
                s.FilePath, s.RepositoryName, s.StartLine, s.EndLine, s.Language,
                s.Ref, s.ResolvedCommitSha
            })
        };
    }

    [McpServerTool(Name = "code_index_commit_history"), Description("Get full commit log and tags for a repository")]
    public async Task<object> GetCommitHistory(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.CommitHistory, "Commit history");
    }

    [McpServerTool(Name = "code_index_ownership"), Description("Get ownership and collaboration info for a repository")]
    public async Task<object> GetOwnership(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Ownership, "Ownership");
    }

    [McpServerTool(Name = "code_index_security"), Description("Get security architecture and auth analysis for a repository")]
    public async Task<object> GetSecurity(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Security, "Security");
    }

    [McpServerTool(Name = "code_index_operations"), Description("Get deployment, CI/CD, and infrastructure info for a repository")]
    public async Task<object> GetOperations(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Operations, "Operations");
    }

    [McpServerTool(Name = "code_index_quality"), Description("Get test strategy, quality signals, and coverage info for a repository")]
    public async Task<object> GetQuality(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Quality, "Quality");
    }

    [McpServerTool(Name = "code_index_dependencies"), Description("Get package dependencies for a repository")]
    public async Task<object> GetDependencies(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.Dependencies, "Dependencies");
    }

    [McpServerTool(Name = "code_index_insights"), Description("Get repository insight layers (architecture, design, security, testing, deployment, etc.)")]
    public async Task<object> GetInsights(
        [Description("Repository URL or name")] string repo_url,
        [Description("Specific layer to retrieve (featuremap, architectureanalysis, designanalysis, implementationanalysis, dependencyanalysis, testanalysis, securityanalysis, deploymentanalysis, operationsanalysis, localsetupguide, techstack). Omit for all layers.")] string? layer = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var layerMap = new Dictionary<string, EnrichmentSubtype>(StringComparer.OrdinalIgnoreCase)
        {
            ["featuremap"] = EnrichmentSubtype.FeatureMap,
            ["architectureanalysis"] = EnrichmentSubtype.ArchitectureAnalysis,
            ["designanalysis"] = EnrichmentSubtype.DesignAnalysis,
            ["implementationanalysis"] = EnrichmentSubtype.ImplementationAnalysis,
            ["dependencyanalysis"] = EnrichmentSubtype.DependencyAnalysis,
            ["testanalysis"] = EnrichmentSubtype.TestAnalysis,
            ["securityanalysis"] = EnrichmentSubtype.SecurityAnalysis,
            ["deploymentanalysis"] = EnrichmentSubtype.DeploymentAnalysis,
            ["operationsanalysis"] = EnrichmentSubtype.OperationsAnalysis,
            ["localsetupguide"] = EnrichmentSubtype.LocalSetupGuide,
            ["techstack"] = EnrichmentSubtype.TechStack,
        };

        if (!string.IsNullOrEmpty(layer))
        {
            if (!layerMap.TryGetValue(layer, out var subtype))
                return new { error = $"Unknown layer '{layer}'. Valid: {string.Join(", ", layerMap.Keys)}" };

            return await GetEnrichmentBySubtype(repo_url, subtype, layer);
        }

        // Return all layers
        var results = new Dictionary<string, object?>();
        foreach (var (name, subtype) in layerMap)
        {
            var enrichments = await _enrichmentService.QueryAsync(
                subtype: subtype, repositoryId: repo.Id, limit: 1);
            results[name] = enrichments.Count > 0
                ? new { enrichments[0].Title, enrichments[0].Content, enrichments[0].Quality }
                : null;
        }

        return new { repository = repo.Name, layers = results };
    }

    [McpServerTool(Name = "code_index_tech_stack"), Description("Get technology stack detection and summary for a repository")]
    public async Task<object> GetTechStack(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.TechStack, "Tech stack");
    }

    [McpServerTool(Name = "code_index_feature_map"), Description("Get the structured feature inventory for a repository")]
    public async Task<object> GetFeatureMap(
        [Description("Repository URL or name")] string repo_url)
    {
        return await GetEnrichmentBySubtype(repo_url, EnrichmentSubtype.FeatureMap, "Feature map");
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

    [McpServerTool(Name = "code_index_add_repository"), Description("Add a Git repository for indexing")]
    public async Task<object> AddRepository(
        [Description("Repository URL (e.g., https://github.com/org/repo)")] string url,
        [Description("Personal access token for private repos")] string? pat = null)
    {
        try
        {
            var repo = await _repoService.AddAsync(new CreateRepositoryRequest { Url = url, PersonalAccessToken = pat });
            return new { repository = repo.Name, id = repo.Id, status = repo.Status, url = repo.Url, message = "Repository added. Indexing pipeline started." };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return new { error = ex.Message };
        }
        catch (UriFormatException)
        {
            return new { error = "Invalid repository URL format." };
        }
    }

    [McpServerTool(Name = "code_index_delete_repository"), Description("Remove a repository and all its indexed data")]
    public async Task<object> DeleteRepository(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        try
        {
            await _repoService.DeleteAsync(repo.Id);
            return new { repository = repo.Name, message = "Repository deleted." };
        }
        catch (KeyNotFoundException)
        {
            return new { error = $"Repository '{repo_url}' not found" };
        }
    }

    [McpServerTool(Name = "code_index_sync_repository"), Description("Trigger a sync/re-index for a repository")]
    public async Task<object> SyncRepository(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        try
        {
            await _repoService.SyncAsync(repo.Id);
            return new { repository = repo.Name, message = "Sync started. Check task queue for progress." };
        }
        catch (InvalidOperationException ex)
        {
            return new { error = ex.Message };
        }
    }

    [McpServerTool(Name = "code_index_commits"), Description("List recent commits for a repository")]
    public async Task<object> ListCommits(
        [Description("Repository URL or name")] string repo_url,
        [Description("Maximum commits to return")] int? limit = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var commits = await _commitRepo.GetByRepositoryAsync(repo.Id, 0, limit ?? 20);
        return new
        {
            repository = repo.Name,
            total = commits.Count,
            commits = commits.Select(c => new { c.Sha, c.Message, c.AuthorName, c.AuthorEmail, c.CommittedAt })
        };
    }

    [McpServerTool(Name = "code_index_search_filters"), Description("Get available repositories and programming languages for search filtering")]
    public async Task<object> GetSearchFilters()
    {
        var filters = await _searchService.GetFilterOptionsAsync();
        return new
        {
            repositories = filters.Repositories.Select(r => new { r.Id, r.Name }),
            languages = filters.Languages
        };
    }

    [McpServerTool(Name = "code_index_enrichment_counts"), Description("Get enrichment counts grouped by subtype, optionally filtered by repository")]
    public async Task<object> GetEnrichmentCounts(
        [Description("Repository URL or name (optional)")] string? repo_url = null)
    {
        Guid? repoId = null;
        if (repo_url is not null)
        {
            var repo = await ResolveRepo(repo_url);
            if (repo is not null) repoId = repo.Id;
        }

        var counts = await _enrichmentService.GetCountsBySubtypeAsync(repositoryId: repoId);
        return new { total = counts.Values.Sum(), counts };
    }

    [McpServerTool(Name = "code_index_get_repository"), Description("Get detailed information about a specific repository including branches, tags, and stats")]
    public async Task<object> GetRepository(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var details = await _repoService.GetDetailsByIdAsync(repo.Id);
        if (details is null)
            return new { error = $"Repository '{repo_url}' not found" };

        return new
        {
            details.Id, details.Name, details.Url,
            provider = details.Provider.ToString(),
            details.DefaultBranch, details.LastIndexedCommitSha,
            details.LastSyncedAt, details.Status,
            stats = details.Stats,
            branches = details.Branches?.Select(b => new { b.Name, b.HeadCommitSha, b.IsDefault }),
            tags = details.Tags?.Select(t => new { t.Name, t.CommitSha })
        };
    }

    [McpServerTool(Name = "code_index_hybrid_search"), Description("Search code using hybrid mode combining semantic similarity and keyword matching via Reciprocal Rank Fusion")]
    public async Task<object> HybridSearch(
        [Description("Search query")] string query,
        [Description("Programming language filter")] string? language = null,
        [Description("Repository URL to search within")] string? source_repo = null,
        [Description("Maximum results to return")] int? limit = null)
    {
        var filter = await BuildSearchFilter(language, source_repo);
        var results = await _searchService.HybridSearchAsync(query, filter, limit ?? 10);
        return FormatSearchResults(results);
    }

    [McpServerTool(Name = "code_index_query_enrichments"), Description("Query enrichments with filters for type, subtype, repository, language, and file path")]
    public async Task<object> QueryEnrichments(
        [Description("Enrichment type filter (e.g., Code, Documentation)")] string? type = null,
        [Description("Enrichment subtype filter (e.g., Chunk, APIDocs, Physical)")] string? subtype = null,
        [Description("Repository URL or name")] string? repo_url = null,
        [Description("Programming language filter")] string? language = null,
        [Description("File path filter")] string? file_path = null,
        [Description("Offset for pagination")] int? offset = null,
        [Description("Maximum results to return")] int? limit = null)
    {
        Guid? repoId = null;
        if (repo_url is not null)
        {
            var repo = await ResolveRepo(repo_url);
            if (repo is not null) repoId = repo.Id;
        }

        EnrichmentType? parsedType = type is not null && Enum.TryParse<EnrichmentType>(type, true, out var t) ? t : null;
        EnrichmentSubtype? parsedSubtype = subtype is not null && Enum.TryParse<EnrichmentSubtype>(subtype, true, out var s) ? s : null;

        var results = await _enrichmentService.QueryAsync(
            parsedType, parsedSubtype, repoId, null, language, file_path, offset ?? 0, limit ?? 50);
        var total = await _enrichmentService.QueryCountAsync(
            parsedType, parsedSubtype, repoId, null, language, file_path);

        return new
        {
            results = results.Select(r => new { r.Id, r.Title, r.Content, r.FilePath, r.Language, r.StartLine, r.EndLine, type = r.Type.ToString(), subtype = r.Subtype.ToString() }),
            totalCount = total,
            offset = offset ?? 0,
            limit = limit ?? 50
        };
    }

    [McpServerTool(Name = "code_index_get_enrichment"), Description("Get a specific enrichment by its ID with full content")]
    public async Task<object> GetEnrichment(
        [Description("Enrichment ID (GUID)")] string enrichment_id)
    {
        if (!Guid.TryParse(enrichment_id, out var id))
            return new { error = "Invalid enrichment ID format. Expected a GUID." };

        var enrichment = await _enrichmentService.GetByIdAsync(id);
        if (enrichment is null)
            return new { error = $"Enrichment '{enrichment_id}' not found" };

        return new
        {
            enrichment.Id, enrichment.Title, enrichment.Content,
            enrichment.FilePath, enrichment.Language,
            enrichment.StartLine, enrichment.EndLine,
            type = enrichment.Type.ToString(),
            subtype = enrichment.Subtype.ToString(),
            enrichment.RepositoryName, enrichment.Quality,
            enrichment.CreatedAt
        };
    }

    [McpServerTool(Name = "code_index_chat_suggestions"), Description("Get suggested questions organized by dimension for the chat interface")]
    public object GetChatSuggestions()
    {
        var suggestions = _questionClassifier.GetSuggestions();
        return new
        {
            dimensions = suggestions.Select(d => new
            {
                d.Id, d.Label,
                questions = d.Questions.Select(q => new { q.Id, q.Text })
            })
        };
    }

    [McpServerTool(Name = "code_index_chat_status"), Description("Check if the chat feature is available (LLM configured)")]
    public object GetChatStatus()
    {
        return new
        {
            available = _chatService.IsAvailable,
            fileAccessEnabled = _chatService.FileAccessEnabled
        };
    }

    [McpServerTool(Name = "code_index_fetch_file"), Description("Fetch a source code file from a repository at a specific git ref (branch, tag, or commit SHA)")]
    public async Task<object> FetchFile(
        [Description("Repository URL or name")] string repo_url,
        [Description("Git ref: branch name, tag, or commit SHA (e.g., 'main', 'v1.0', 'abc1234')")] string @ref,
        [Description("Path to the file relative to repository root (e.g., 'src/Program.cs')")] string file_path)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var result = await _chatFileAccessService.FetchFileForChatAsync(
            repo.Id, @ref, file_path, ct: default);

        if (!result.IsSuccess)
        {
            var errorObj = new Dictionary<string, object> { ["error"] = result.Error! };
            if (result.IsBinary)
                errorObj["metadata"] = new { size = result.Size };
            if (result.Error!.Contains("not found", StringComparison.OrdinalIgnoreCase))
                errorObj["suggestion"] = "Use code_index_grep or code_index_ls to find the correct path";
            else if (result.Error.Contains("too large", StringComparison.OrdinalIgnoreCase))
                errorObj["suggestion"] = "Use code_index_grep to search for specific patterns";
            return errorObj;
        }

        return new
        {
            file_path = result.FilePath,
            content = result.Content,
            language = result.Language,
            size = result.Size,
            resolved_sha = result.ResolvedSha,
            line_count = result.Content?.Split('\n').Length ?? 0
        };
    }

    [McpServerTool(Name = "code_index_queue_tasks"), Description("List all indexing tasks in the queue with their status and progress")]
    public async Task<object> ListQueueTasks()
    {
        var tasks = await _taskRepo.GetAllAsync();
        return new
        {
            total = tasks.Count,
            tasks = tasks
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id, t.RepositoryId, t.CommitId,
                    operation = t.Operation.ToString(),
                    status = t.Status.ToString(),
                    t.Progress, t.ErrorMessage, t.ChainId, t.Priority,
                    t.CreatedAt, t.StartedAt, t.CompletedAt
                })
        };
    }

    [McpServerTool(Name = "code_index_queue_task"), Description("Get details of a specific indexing task by ID")]
    public async Task<object> GetQueueTask(
        [Description("Task ID (GUID)")] string task_id)
    {
        if (!Guid.TryParse(task_id, out var id))
            return new { error = "Invalid task ID format. Expected a GUID." };

        var task = await _taskRepo.GetByIdAsync(id);
        if (task is null)
            return new { error = $"Task '{task_id}' not found" };

        return new
        {
            task.Id, task.RepositoryId, task.CommitId,
            operation = task.Operation.ToString(),
            status = task.Status.ToString(),
            task.Progress, task.ErrorMessage, task.ChainId, task.Priority,
            task.CreatedAt, task.StartedAt, task.CompletedAt
        };
    }

    [McpServerTool(Name = "code_index_get_commit"), Description("Get details of a specific commit by SHA")]
    public async Task<object> GetCommit(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA")] string sha)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var commit = await _commitRepo.GetByShaAsync(repo.Id, sha);
        if (commit is null)
            return new { error = $"Commit '{sha}' not found in repository '{repo_url}'" };

        return new
        {
            commit.Id, commit.Sha, commit.Message,
            commit.AuthorName, commit.AuthorEmail,
            commit.CommittedAt, commit.IsIndexed
        };
    }

    [McpServerTool(Name = "code_index_discover_github"), Description("Discover repositories in a GitHub organization")]
    public async Task<object> DiscoverGitHub(
        [Description("GitHub organization name")] string org,
        [Description("Personal access token (optional, for private repos)")] string? pat = null,
        [Description("Exclude archived repositories")] bool? exclude_archived = null,
        [Description("Exclude forked repositories")] bool? exclude_forks = null)
    {
        var repos = await _discoveryService.DiscoverGitHubAsync(
            org, pat, exclude_archived ?? true, exclude_forks ?? true);
        return new
        {
            organization = org,
            total = repos.Count,
            repositories = repos.Select(r => new
            {
                r.Name, r.FullName, r.CloneUrl, r.Provider,
                r.DefaultBranch, r.Description,
                r.IsArchived, r.IsFork, r.AlreadyTracked
            })
        };
    }

    [McpServerTool(Name = "code_index_discover_azure_devops"), Description("Discover repositories in an Azure DevOps organization")]
    public async Task<object> DiscoverAzureDevOps(
        [Description("Azure DevOps organization name")] string org,
        [Description("Project name (optional, discovers all projects if omitted)")] string? project = null,
        [Description("Personal access token (optional)")] string? pat = null)
    {
        var repos = await _discoveryService.DiscoverAzureDevOpsAsync(org, project, pat);
        return new
        {
            organization = org,
            project,
            total = repos.Count,
            repositories = repos.Select(r => new
            {
                r.Name, r.FullName, r.CloneUrl, r.Provider,
                r.DefaultBranch, r.Description,
                r.IsArchived, r.IsFork, r.AlreadyTracked
            })
        };
    }

    [McpServerTool(Name = "code_index_discover_sync"), Description("Add discovered repositories for indexing")]
    public async Task<object> SyncDiscovered(
        [Description("List of repository URLs to add")] List<string> repository_urls,
        [Description("Personal access token for private repos")] string? pat = null)
    {
        var added = new List<object>();
        var skipped = new List<string>();

        foreach (var url in repository_urls)
        {
            try
            {
                var repo = await _repoService.AddAsync(
                    new CreateRepositoryRequest { Url = url, PersonalAccessToken = pat });
                added.Add(new { repo.Id, repo.Name, repo.Url, repo.Status });
            }
            catch (InvalidOperationException)
            {
                skipped.Add(url);
            }
        }

        return new
        {
            added = added,
            addedCount = added.Count,
            skipped = skipped,
            skippedCount = skipped.Count
        };
    }

    [McpServerTool(Name = "code_index_indexing_history"), Description("Get indexing run history for a repository")]
    public async Task<object> GetIndexingHistory(
        [Description("Repository URL or name")] string repo_url,
        [Description("Maximum history entries to return")] int? limit = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var runs = await _dbContext.IndexingRuns
            .Where(r => r.RepositoryId == repo.Id)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit ?? 20)
            .Select(r => new
            {
                r.Id, r.RepositoryId,
                r.StartedAt, r.CompletedAt,
                durationSeconds = r.CompletedAt.HasValue
                    ? (r.CompletedAt.Value - r.StartedAt).TotalSeconds
                    : (double?)null,
                r.Status,
                r.SnippetsAdded, r.SnippetsUpdated,
                r.SnippetsDeleted, r.SnippetsUnchanged,
                r.ApiDocsGenerated, r.CommitsScanned,
                r.ErrorMessage
            })
            .ToListAsync();

        return new
        {
            repository = repo.Name,
            total = runs.Count,
            runs
        };
    }

    [McpServerTool(Name = "code_index_git_log"), Description("Get live git commit log with enrichment counts, cursor-paginated")]
    public async Task<object> GitLog(
        [Description("Repository URL or name")] string repo_url,
        [Description("Git ref (branch, tag, or SHA) to start from")] string? @ref = null,
        [Description("Maximum commits to return")] int? limit = null,
        [Description("Cursor SHA for pagination (commits before this SHA)")] string? before = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        if (!Directory.Exists(cloneDir))
            return new { error = "Repository not cloned yet" };

        var effectiveRef = @ref ?? repo.DefaultBranch ?? "HEAD";
        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 500);

        var resolvedSha = await _gitService.ResolveRefAsync(cloneDir, effectiveRef);
        if (resolvedSha is null)
            return new { error = $"Ref '{effectiveRef}' not found" };

        List<GitCommitInfo> commits;
        try
        {
            commits = await _gitService.GetCommitsAsync(cloneDir, effectiveRef, effectiveLimit + 1, before);
        }
        catch (InvalidOperationException ex)
        {
            return new { error = ex.Message };
        }

        var hasMore = commits.Count > effectiveLimit;
        if (hasMore)
            commits = commits.Take(effectiveLimit).ToList();

        // Batch query enrichment counts from DB
        var shas = commits.Select(c => c.Sha).ToList();
        var dbCommits = await _dbContext.Commits
            .Where(c => c.RepositoryId == repo.Id && shas.Contains(c.Sha))
            .Select(c => new { c.Sha, c.IsIndexed, c.Id })
            .ToListAsync();
        var commitIdsBySha = dbCommits.ToDictionary(c => c.Sha, c => c.Id);
        var indexedShas = dbCommits.Where(c => c.IsIndexed).Select(c => c.Sha).ToHashSet();
        var commitIds = commitIdsBySha.Values.ToList();
        var enrichmentCounts = commitIds.Count > 0
            ? await _dbContext.Enrichments
                .Where(e => e.CommitId.HasValue && commitIds.Contains(e.CommitId.Value))
                .GroupBy(e => e.CommitId!.Value)
                .Select(g => new { CommitId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.CommitId, g => g.Count)
            : new Dictionary<Guid, int>();

        return new
        {
            hasMore,
            nextCursor = hasMore ? commits.Last().Sha : null,
            commits = commits.Select(c =>
            {
                commitIdsBySha.TryGetValue(c.Sha, out var commitId);
                enrichmentCounts.TryGetValue(commitId, out var enrichCount);
                return new
                {
                    sha = c.Sha,
                    abbreviatedSha = c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha,
                    message = c.Message,
                    authorName = c.AuthorName,
                    authorEmail = c.AuthorEmail,
                    committedAt = c.CommittedAt,
                    parentShas = c.ParentShas,
                    isIndexed = indexedShas.Contains(c.Sha),
                    enrichmentCount = enrichCount
                };
            })
        };
    }

    [McpServerTool(Name = "code_index_git_refs"), Description("List branches and tags for a repository")]
    public async Task<object> GitRefs(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        if (!Directory.Exists(cloneDir))
            return new { error = "Repository not cloned yet" };

        var branches = await _gitService.GetBranchesAsync(cloneDir);
        var tags = await _gitService.GetTagsAsync(cloneDir);
        var head = await _gitService.GetHeadRefAsync(cloneDir);

        return new
        {
            head,
            branches = branches.Select(b => new { b.Name, sha = b.HeadCommitSha, b.IsDefault }),
            tags = tags.Select(t => new { t.Name, sha = t.CommitSha })
        };
    }

    [McpServerTool(Name = "code_index_git_tree"), Description("List file tree at a specific git ref with enrichment status")]
    public async Task<object> GitTree(
        [Description("Repository URL or name")] string repo_url,
        [Description("Git ref (branch, tag, or SHA)")] string @ref,
        [Description("Subdirectory path to list (optional)")] string? path = null,
        [Description("List recursively (default: false)")] bool? recursive = null)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        if (!Directory.Exists(cloneDir))
            return new { error = "Repository not cloned yet" };

        var resolvedSha = await _gitService.ResolveRefAsync(cloneDir, @ref);
        if (resolvedSha is null)
            return new { error = $"Ref '{@ref}' not found" };

        List<GitTreeEntry> entries;
        try
        {
            entries = await _gitService.ListTreeAsync(cloneDir, @ref, path, recursive ?? false);
        }
        catch (InvalidOperationException)
        {
            return new { error = $"Path '{path}' not found at ref '{@ref}'" };
        }

        // Get enrichment file paths
        var dbCommit = await _commitRepo.GetByShaAsync(repo.Id, resolvedSha);
        var enrichedPaths = new HashSet<string>();
        if (dbCommit is not null)
        {
            enrichedPaths = (await _dbContext.Enrichments
                .Where(e => e.CommitId == dbCommit.Id && e.FilePath != null)
                .Select(e => e.FilePath!)
                .Distinct()
                .ToListAsync())
                .ToHashSet();
        }

        return new
        {
            @ref,
            path,
            recursive = recursive ?? false,
            entries = entries.Select(e => new
            {
                e.Path, e.Name, e.Type, e.Hash, e.Size, e.Language,
                hasEnrichments = e.Type == "blob" && enrichedPaths.Contains(e.Path)
            })
        };
    }

    [McpServerTool(Name = "code_index_commit_summary"), Description("Get enrichment summary for a specific commit")]
    public async Task<object> CommitSummary(
        [Description("Repository URL or name")] string repo_url,
        [Description("Commit SHA")] string sha)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var commit = await _commitRepo.GetByShaAsync(repo.Id, sha);
        if (commit is null)
            return new { error = $"Commit '{sha}' not found in repository '{repo_url}'" };

        var countsBySubtype = await _dbContext.Enrichments
            .Where(e => e.CommitId == commit.Id)
            .GroupBy(e => e.Subtype)
            .Select(g => new { Subtype = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(g => g.Subtype, g => g.Count);

        var filesIndexed = await _dbContext.RepositoryFiles
            .CountAsync(f => f.CommitId == commit.Id);

        var enrichmentIds = await _dbContext.Enrichments
            .Where(e => e.CommitId == commit.Id)
            .Select(e => e.Id)
            .ToListAsync();

        var embeddingsCount = enrichmentIds.Count > 0
            ? await _dbContext.ContentEmbeddings
                .CountAsync(ce => enrichmentIds.Contains(ce.EnrichmentId))
            : 0;

        return new
        {
            sha = commit.Sha,
            isIndexed = commit.IsIndexed,
            totalEnrichments = countsBySubtype.Values.Sum(),
            filesIndexed,
            embeddingsCount,
            countsBySubtype
        };
    }

    [McpServerTool(Name = "code_index_committers"), Description("Get unique committers/contributors for a repository with commit counts")]
    public async Task<object> GetCommitters(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        var committers = await _dbContext.Commits
            .Where(c => c.RepositoryId == repo.Id)
            .GroupBy(c => new { c.AuthorName, c.AuthorEmail })
            .Select(g => new
            {
                name = g.Key.AuthorName,
                email = g.Key.AuthorEmail,
                commits = g.Count(),
                firstCommit = g.Min(c => c.CommittedAt),
                lastCommit = g.Max(c => c.CommittedAt)
            })
            .OrderByDescending(c => c.commits)
            .ToListAsync();

        var totalCommits = committers.Sum(c => c.commits);

        return new
        {
            repository = repo.Name,
            totalCommits,
            uniqueCommitters = committers.Count,
            committers
        };
    }

    [McpServerTool(Name = "code_index_report"), Description("Get the full insight analysis report for a repository with ratings, feedback, health score, and improvements")]
    public async Task<object> GetReport(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        try
        {
            var report = await _reportService.GenerateReportAsync(repo.Id);
            return new
            {
                repository = report.RepositoryName,
                generatedAt = report.GeneratedAt,
                overallHealthScore = report.OverallHealthScore,
                velocity = report.Velocity,
                layers = report.Layers.Select(l => new
                {
                    l.Name, l.Subtype,
                    l.MaturityRating, l.QualityRating, l.RiskRating,
                    l.Strengths, l.Weaknesses, l.Recommendations,
                    l.HasMermaidDiagrams
                }),
                top5Improvements = report.Top5Improvements
            };
        }
        catch (InvalidOperationException ex)
        {
            return new { error = ex.Message };
        }
    }

    [McpServerTool(Name = "code_index_health_score"), Description("Get the overall health score and top improvements for a repository")]
    public async Task<object> GetHealthScore(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        try
        {
            var report = await _reportService.GenerateReportAsync(repo.Id);
            return new
            {
                repository = report.RepositoryName,
                overallHealthScore = report.OverallHealthScore,
                velocity = report.Velocity,
                top5Improvements = report.Top5Improvements
            };
        }
        catch (InvalidOperationException ex)
        {
            return new { error = ex.Message };
        }
    }

    [McpServerTool(Name = "code_index_settings"), Description("Get current provider configuration including API server URLs, models, and key status for both embedding and LLM providers")]
    public async Task<object> GetSettings()
    {
        var (embeddingKey, embeddingUrl, embeddingModel, embeddingSource) = await _apiKeyResolver.ResolveEmbeddingKeyAsync(null);
        var (llmKey, llmUrl, llmModel, llmSource) = await _apiKeyResolver.ResolveLlmKeyAsync(null);

        return new
        {
            embedding = new
            {
                baseUrl = embeddingUrl,
                model = embeddingModel,
                hasKey = !string.IsNullOrEmpty(embeddingKey),
                source = embeddingSource
            },
            llm = new
            {
                baseUrl = llmUrl,
                model = llmModel,
                hasKey = !string.IsNullOrEmpty(llmKey),
                source = llmSource
            }
        };
    }

    [McpServerTool(Name = "code_index_update_settings"), Description("Update provider configuration (server URL and model) for embedding or LLM providers. Does not accept API keys for security.")]
    public async Task<object> UpdateSettings(
        [Description("Embedding provider server URL (e.g., https://api.openai.com/v1, http://localhost:11434/v1)")] string? embedding_base_url = null,
        [Description("Embedding model name (e.g., text-embedding-3-small)")] string? embedding_model = null,
        [Description("LLM provider server URL")] string? llm_base_url = null,
        [Description("LLM model name (e.g., gpt-4o-mini)")] string? llm_model = null)
    {
        // Validate URLs
        if (embedding_base_url is not null && !SettingsController.IsValidBaseUrl(embedding_base_url))
            return new { error = "Invalid embedding base URL. Must be a valid HTTP or HTTPS URL." };
        if (llm_base_url is not null && !SettingsController.IsValidBaseUrl(llm_base_url))
            return new { error = "Invalid LLM base URL. Must be a valid HTTP or HTTPS URL." };

        // Use "anonymous" as the user for MCP tool calls
        const string userId = "anonymous";
        var settings = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);

        if (settings is null)
        {
            settings = new Domain.Entities.UserSettings
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.UserSettings.Add(settings);
        }

        var changes = new List<string>();

        if (embedding_base_url is not null)
        {
            settings.EmbeddingBaseUrl = embedding_base_url == "" ? null : embedding_base_url;
            changes.Add($"EmbeddingBaseUrl: {embedding_base_url}");
        }
        if (embedding_model is not null)
        {
            settings.EmbeddingModel = embedding_model == "" ? null : embedding_model;
            changes.Add($"EmbeddingModel: {embedding_model}");
        }
        if (llm_base_url is not null)
        {
            settings.LlmBaseUrl = llm_base_url == "" ? null : llm_base_url;
            changes.Add($"LlmBaseUrl: {llm_base_url}");
        }
        if (llm_model is not null)
        {
            settings.LlmModel = llm_model == "" ? null : llm_model;
            changes.Add($"LlmModel: {llm_model}");
        }

        if (changes.Count == 0)
            return new { message = "No changes specified." };

        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new { message = "Settings updated.", changes };
    }

    [McpServerTool(Name = "code_index_wipe_enrichments"), Description("Delete all enrichments for a repository. Requires re-sync to regenerate.")]
    public async Task<object> WipeEnrichments(
        [Description("Repository URL or name")] string repo_url)
    {
        var repo = await ResolveRepo(repo_url);
        if (repo is null)
            return new { error = $"Repository '{repo_url}' not found" };

        try
        {
            await _repoService.WipeEnrichmentsAsync(repo.Id);
            return new { success = true, message = $"All enrichments wiped for {repo.Name}. Run sync to regenerate." };
        }
        catch (InvalidOperationException ex)
        {
            return new { error = ex.Message };
        }
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
