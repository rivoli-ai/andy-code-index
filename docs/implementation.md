# Andy.CodeIndex — Implementation Guide

## 1. Implementation Order

The epics should be implemented in dependency order. Within each epic, features and stories follow the sequence below.

### Phase 1: Foundation (Epic #1)
**Goal:** Buildable solution with database, Docker, and health check.

```
#5  Create solution and project files
#6  Configure NuGet packages
#8  Design domain entities
#9  Create DbContext and configurations
#10 Create initial migration
#11 Implement repository pattern
#7  Set up DI and Program.cs
#14 Configure environment-based settings
#12 Create Dockerfile
#13 Create docker-compose
```

**Exit Criteria:** `dotnet build` succeeds, `docker-compose up` starts API + PostgreSQL, `/health` returns 200.

### Phase 2: Repository Management (Epic #15)
**Goal:** Add, list, sync, and delete repositories.

```
#23 Implement IGitService (clone/fetch/pull)
#20 Implement IRepositoryService
#19 Implement RepositoriesController
#24 File reading and listing at commits
#25 Git grep
#21 Unit tests for repo management
#22 Integration tests for repo API
#26 Unit tests for git operations
#27 Implement periodic sync service
#28 Selective reindexing
#29 Unit tests for sync
```

**Exit Criteria:** Can add a GitHub repo via API, clone succeeds, repo appears in list, can be deleted.

### Phase 3: Indexing Pipeline (Epic #30)
**Goal:** Chunk code, generate embeddings, create search indexes.

```
#36 Implement IChunkingService
#37 Unit tests for chunking
#42 Implement ITaskQueue
#43 Implement BackgroundWorkerService
#38 Implement IEmbeddingService
#39 Implement OpenAI embedding provider
#40 Implement pgvector storage
#41 Unit tests for embedding
#44 Implement task handlers
#45 Implement task chaining
#46 Unit tests for queue and worker
#52 Implement ICodeAnalysisService
#53 Generate API docs from AST
#54 Unit tests for code analysis
#47 Implement IEnrichmentService
#48 Architecture and DB schema enrichments
#49 Cookbook and wiki enrichments
#50 Commit description enrichments
#51 Unit tests for enrichments
```

**Exit Criteria:** Adding a repo triggers full chain: clone → scan → chunk → embed → enrich. Task queue shows progress.

### Phase 4: Search (Epic #55) + Enrichment API (Epic #129)
**Goal:** Semantic, keyword, and hybrid search working.

```
#60 Implement semantic search
#62 Implement BM25 keyword search
#64 Implement RRF algorithm
#65 Implement hybrid search orchestrator
#67 Implement ls and grep endpoints
#131 Implement EnrichmentsController
#132 Implement CommitsController
#61 Unit tests for semantic search
#63 Unit tests for keyword search
#66 Unit tests for hybrid search
#68 Unit tests for ls and grep
#133 Unit tests for enrichment API
```

**Exit Criteria:** All three search modes return relevant results. Enrichments queryable with filters.

### Phase 5: MCP Server (Epic #69)
**Goal:** All 14 MCP tools working with authentication.

```
#77 Configure MCP server endpoint with auth
#72 Version and repository listing tools
#73 Documentation tools
#74 Search tools
#75 Resource reading tool
#76 Unit tests for MCP tools
#78 Integration tests for MCP
```

**Exit Criteria:** MCP client can connect, list tools, invoke search, and read resources.

### Phase 6: Auth & RBAC (Epic #79)
**Goal:** Full authentication and permission enforcement.

```
#83 Configure JWT Bearer via Andy.Auth
#88 Implement ICurrentUserService
#85 Define permission model in RBAC
#86 Implement RBAC checks in controllers
#89 Implement authorization policies
#84 Unit tests for auth
#87 Unit tests for RBAC
#90 Integration tests for auth flow
```

**Exit Criteria:** Unauthenticated returns 401, unauthorized returns 403, instance-level permissions work.

### Phase 7: Frontend (Epic #91)
**Goal:** Angular app with all features.

```
#97  Scaffold Angular app
#98  Auth flow
#99  Shared UI components
#100 Frontend service tests
#101 Repository list page
#102 Add repository form
#103 Repository detail page
#104 Repository management tests
#105 Search page
#106 Search results with highlighting
#107 File viewer
#108 Search tests
#109 Enrichment browser
#110 Wiki viewer
#111 Enrichment tests
#112 Task queue dashboard
#113 Task dashboard tests
```

**Exit Criteria:** User can log in, add repo, search code, browse enrichments, view tasks.

### Phase 8: CLI & Ecosystem (Epics #114, #120)
**Goal:** CLI tool and rivoli-ai integration.

```
#116 Scaffold CLI
#117 Repo management commands
#118 Search and enrichment commands
#119 CLI tests
#123 GitHub org discovery
#124 Seed rivoli-ai repos
#126 Optimize MCP for agents
#127 Cross-repo search
#125 Integration test: multi-repo
#128 Integration test: MCP client
```

**Exit Criteria:** CLI can manage repos and search. All rivoli-ai repos indexed and searchable.

## 2. Key Implementation Details

### 2.1 Program.cs Setup

Following the andy-docs pattern:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<CodeIndexDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

// Authentication (Andy.Auth)
builder.Services.AddAndyAuth(builder.Configuration);

// RBAC (Andy.Rbac.Client)
builder.Services.AddRbacClient(options => {
    options.BaseUrl = builder.Configuration["Rbac:BaseUrl"];
});

// MCP Server
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Swagger
builder.Services.AddSwaggerGen(options => {
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(/* ... */);
});

// Application services
builder.Services.AddScoped<IRepositoryService, RepositoryService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IGitService, GitService>();
builder.Services.AddScoped<IChunkingService, ChunkingService>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IEnrichmentService, EnrichmentService>();
builder.Services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
builder.Services.AddScoped<ITaskQueue, TaskQueue>();
builder.Services.AddScoped<IRankFusionService, RankFusionService>();
builder.Services.AddScoped<IVectorStore, PgVectorStore>();

// Embedding provider
builder.Services.AddSingleton<IEmbeddingProvider, OpenAiEmbeddingProvider>();

// Background workers
builder.Services.AddHostedService<BackgroundWorkerService>();
builder.Services.AddHostedService<PeriodicSyncService>();

// Options
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection("Chunking"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));
builder.Services.Configure<IndexingOptions>(builder.Configuration.GetSection("Indexing"));

// CORS
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAngularApp", policy => { /* ... */ });
    options.AddPolicy("AllowMcpClients", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Middleware
app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Endpoints
app.MapControllers();
app.MapMcp("/mcp").RequireCors("AllowMcpClients").RequireAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

// Auto-migrate in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
```

### 2.2 MCP Tool Implementation

Following the `[McpServerToolType]` pattern from andy-docs:

```csharp
[McpServerToolType]
public class CodeIndexTools
{
    private readonly IRepositoryService _repositoryService;
    private readonly ISearchService _searchService;
    private readonly IEnrichmentService _enrichmentService;
    private readonly IGitService _gitService;

    public CodeIndexTools(
        IRepositoryService repositoryService,
        ISearchService searchService,
        IEnrichmentService enrichmentService,
        IGitService gitService)
    {
        _repositoryService = repositoryService;
        _searchService = searchService;
        _enrichmentService = enrichmentService;
        _gitService = gitService;
    }

    [McpServerTool(Name = "code_index_version",
        Description = "Get the Andy.CodeIndex server version")]
    public string GetVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
    }

    [McpServerTool(Name = "code_index_repositories",
        Description = "List all repositories tracked by the code index")]
    public async Task<object> ListRepositories()
    {
        var repos = await _repositoryService.GetAllAsync();
        return repos.Select(r => new { r.Id, r.Name, r.Url, /* ... */ });
    }

    [McpServerTool(Name = "code_index_semantic_search",
        Description = "Search code using semantic similarity")]
    public async Task<object> SemanticSearch(
        [Description("Natural language search query")] string query,
        [Description("Programming language filter")] string? language = null,
        [Description("Repository URL to search within")] string? source_repo = null,
        [Description("Maximum results to return")] int? limit = null)
    {
        var filter = new SearchFilter();
        if (language != null) filter.Languages = [language];
        if (source_repo != null) filter.RepositoryIds = [await ResolveRepoId(source_repo)];

        var results = await _searchService.SemanticSearchAsync(query, filter, limit ?? 10);
        return FormatForAgent(results);
    }

    // ... remaining 11 tools follow same pattern
}
```

### 2.3 Chunking Service

Port of Kodit's three-tier algorithm:

```csharp
public class ChunkingService : IChunkingService
{
    public List<CodeChunk> ChunkText(string content, ChunkingOptions options)
    {
        if (string.IsNullOrEmpty(content)) return [];

        var lines = content.Split('\n');
        var chunks = new List<CodeChunk>();
        var currentChunk = new StringBuilder();
        var currentStartLine = 1;
        int currentLine = 1;

        foreach (var line in lines)
        {
            // Size measured in runes (Unicode code points), not string.Length
            var runeCount = currentChunk.ToString().EnumerateRunes().Count();
            if (runeCount + line.EnumerateRunes().Count() + 1 > options.Size && runeCount > 0)
            {
                // Emit chunk
                EmitChunk(chunks, currentChunk, currentStartLine, currentLine - 1);

                // Overlap: keep trailing whole lines within overlap budget (line-granular)
                var overlapLines = GetOverlapLines(currentChunk.ToString(), options.Overlap);
                currentChunk.Clear();
                currentChunk.Append(overlapLines);
                currentStartLine = currentLine - CountLines(overlapLines);
            }

            if (line.Length > options.Size)
            {
                // Tier 2 & 3: split long lines
                SplitLongLine(chunks, line, options, ref currentChunk,
                              ref currentStartLine, currentLine);
            }
            else
            {
                currentChunk.AppendLine(line);
            }
            currentLine++;
        }

        if (currentChunk.Length >= options.MinSize)
            EmitChunk(chunks, currentChunk, currentStartLine, currentLine - 1);

        return chunks;
    }
}
```

### 2.4 Reciprocal Rank Fusion

```csharp
public class RankFusionService : IRankFusionService
{
    private const int DefaultK = 60;

    public List<FusedResult> Fuse(List<RankedResultSet> inputs, int k = DefaultK)
    {
        var scores = new Dictionary<Guid, double>();
        var metadata = new Dictionary<Guid, FusedResult>();

        foreach (var resultSet in inputs)
        {
            for (int rank = 0; rank < resultSet.Results.Count; rank++)
            {
                var item = resultSet.Results[rank];
                var rrf = 1.0 / (k + rank);  // rank is 0-based (matching Kodit)

                if (!scores.ContainsKey(item.EnrichmentId))
                {
                    scores[item.EnrichmentId] = 0;
                    metadata[item.EnrichmentId] = new FusedResult(item);
                }
                scores[item.EnrichmentId] += rrf;
            }
        }

        return metadata.Values
            .Select(r => r with { FusedScore = scores[r.EnrichmentId] })
            .OrderByDescending(r => r.FusedScore)
            .ToList();
    }
}
```

### 2.5 Background Worker and Task Chaining

```csharp
public class BackgroundWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundWorkerService> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ITaskQueue>();
            var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<ITaskHandler>>();

            var task = await queue.DequeueAsync(ct);
            if (task == null)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            var handler = handlers.FirstOrDefault(h => h.Operation == task.Operation);
            if (handler == null)
            {
                _logger.LogWarning("No handler for {Operation}", task.Operation);
                await queue.UpdateStatusAsync(task.Id, TaskStatus.Failed, "No handler");
                continue;
            }

            try
            {
                await queue.UpdateStatusAsync(task.Id, TaskStatus.Running);
                await handler.HandleAsync(task, ct);
                await queue.UpdateStatusAsync(task.Id, TaskStatus.Completed);

                // Chain: enqueue next operation
                await EnqueueNextInChain(queue, task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task {Id} failed", task.Id);
                await queue.UpdateStatusAsync(task.Id, TaskStatus.Failed, ex.Message);
            }
        }
    }

    private async Task EnqueueNextInChain(ITaskQueue queue, IndexingTask completed)
    {
        var next = TaskChains.GetNext(completed.Operation, completed.ChainId);
        if (next.HasValue)
        {
            await queue.EnqueueAsync(new IndexingTask
            {
                RepositoryId = completed.RepositoryId,
                CommitId = completed.CommitId,
                Operation = next.Value,
                ChainId = completed.ChainId
            });
        }
    }
}
```

### 2.6 RBAC Integration in Controllers

```csharp
[ApiController]
[Route("api/v1/repositories")]
[Authorize]
public class RepositoriesController : ControllerBase
{
    private readonly IRepositoryService _service;
    private readonly IRbacClient _rbac;
    private readonly ICurrentUserService _currentUser;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var subjectId = _currentUser.GetSubjectId();
        var allowed = await _rbac.HasPermissionAsync(
            subjectId, "code-index:repository:read");
        if (!allowed) return Forbid("Insufficient permissions");

        var repos = await _service.GetAllAsync();
        return Ok(repos);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var subjectId = _currentUser.GetSubjectId();
        var allowed = await _rbac.HasPermissionAsync(
            subjectId, "code-index:repository:read",
            resourceInstanceId: id.ToString());  // Instance-level check
        if (!allowed) return Forbid("No access to this repository");

        var repo = await _service.GetByIdAsync(id);
        if (repo == null) return NotFound();
        return Ok(repo);
    }

    // ... POST, DELETE, sync follow same pattern
}
```

## 3. NuGet Package Reference

### 3.1 Andy.CodeIndex.Api

```xml
<PackageReference Include="Andy.Auth" Version="2025.11.17-rc.1" />
<PackageReference Include="Andy.Rbac.Client" Version="*" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.11" />
<PackageReference Include="ModelContextProtocol" Version="0.4.0-preview.3" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.4.0-preview.3" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.8.1" />
```

### 3.2 Andy.CodeIndex.Infrastructure

```xml
<PackageReference Include="Andy.Llm" Version="2025.10.30-rc.23" />
<PackageReference Include="Andy.Configuration" Version="2025.7.16-rc.6" />
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.2.1" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
<PackageReference Include="LibGit2Sharp" Version="0.30.0" />
```

### 3.3 Andy.CodeIndex.Domain

```xml
<PackageReference Include="Pgvector" Version="0.3.0" />
<!-- For Vector type on ContentEmbedding entity -->
```

### 3.4 Test Projects

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="Moq" Version="4.20.72" />
<PackageReference Include="FluentAssertions" Version="6.12.2" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
<PackageReference Include="coverlet.collector" Version="6.0.2" />
```

### 3.5 Angular Client

```json
{
  "dependencies": {
    "@angular/common": "^20.3.0",
    "@angular/core": "^20.3.0",
    "@angular/forms": "^20.3.0",
    "@angular/router": "^20.3.0",
    "marked": "^16.4.1",
    "prismjs": "^1.29.0",
    "rxjs": "~7.8.0"
  }
}
```

## 4. Database Migrations

### 4.1 Initial Migration Checklist

The initial migration must:

```sql
-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Key tables
CREATE TABLE "Repositories" (...);
CREATE TABLE "Commits" (...);
CREATE TABLE "Branches" (...);
CREATE TABLE "Tags" (...);
CREATE TABLE "RepositoryFiles" (...);
CREATE TABLE "Enrichments" (
    ...
    "SearchVector" tsvector GENERATED ALWAYS AS (
        to_tsvector('english', coalesce("Content", ''))
    ) STORED,
    ...
);
CREATE TABLE "ContentEmbeddings" (
    ...
    "EmbeddingVector" vector(1536),  -- dimension matches model
    ...
);
CREATE TABLE "IndexingTasks" (...);
CREATE TABLE "ChunkLineRanges" (...);

-- Indexes
CREATE INDEX "IX_Enrichments_SearchVector" ON "Enrichments" USING GIN ("SearchVector");
CREATE INDEX "IX_ContentEmbeddings_Vector" ON "ContentEmbeddings"
    USING hnsw ("EmbeddingVector" vector_cosine_ops);
CREATE INDEX "IX_Enrichments_Type_Subtype" ON "Enrichments" ("Type", "Subtype");
CREATE INDEX "IX_Enrichments_RepositoryId" ON "Enrichments" ("RepositoryId");
CREATE INDEX "IX_IndexingTasks_Status" ON "IndexingTasks" ("Status");
```

### 4.2 Dimension Flexibility

The embedding vector dimension depends on the configured model:
- `text-embedding-3-small`: 1536
- `text-embedding-3-large`: 3072

Consider using a startup probe (like Kodit) to detect dimension from the first embedding and create the column accordingly, or configure it explicitly in `EmbeddingOptions.Dimensions`.

## 5. Testing Conventions

### 5.1 Test Naming

```
MethodUnderTest_StateOrInput_ExpectedResult
```

Examples:
- `GetRepositories_WhenReposExist_ReturnsOkWithList`
- `AddRepository_WithInvalidUrl_Returns422`
- `SemanticSearch_WithLanguageFilter_ReturnsOnlyMatchingLanguage`
- `ChunkText_WithOversizedLine_SplitsOnWhitespace`

### 5.2 Test Organization

```
tests/
├── Andy.CodeIndex.Tests.Unit/
│   ├── Controllers/
│   │   ├── RepositoriesControllerTests.cs
│   │   ├── SearchControllerTests.cs
│   │   └── EnrichmentsControllerTests.cs
│   ├── Services/
│   │   ├── ChunkingServiceTests.cs
│   │   ├── EmbeddingServiceTests.cs
│   │   ├── SearchServiceTests.cs
│   │   ├── RankFusionServiceTests.cs
│   │   └── EnrichmentServiceTests.cs
│   ├── Workers/
│   │   ├── BackgroundWorkerServiceTests.cs
│   │   ├── PeriodicSyncServiceTests.cs
│   │   └── Handlers/
│   ├── Mcp/
│   │   └── CodeIndexToolsTests.cs
│   └── Helpers/
│       ├── TestDbContextFactory.cs
│       └── TestAuthHandler.cs
│
└── Andy.CodeIndex.Tests.Integration/
    ├── RepositoryApiTests.cs
    ├── SearchApiTests.cs
    ├── McpIntegrationTests.cs
    ├── AuthIntegrationTests.cs
    └── Fixtures/
        └── IntegrationTestFixture.cs
```

### 5.3 Test Infrastructure

```csharp
// Reusable InMemory DB factory
public static class TestDbContextFactory
{
    public static CodeIndexDbContext Create()
    {
        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new CodeIndexDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}

// Test auth handler for integration tests
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-id") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

## 6. Resource URI Format

For the `code_index_read_resource` MCP tool:

```
code-index://{repository-name}/{commit-sha}/{file-path}
```

Examples:
- `code-index://andy-docs/abc1234/src/Andy.Docs.Api/Program.cs`
- `code-index://andy-auth/def5678/src/Andy.Auth/Extensions/ServiceCollectionExtensions.cs`

Parsing: split on `/` after `code-index://`, first segment = repo name, second = commit SHA, remainder = file path.

## 7. Environment Variables

All configuration can be overridden via environment variables using the standard ASP.NET Core `__` separator:

```bash
ConnectionStrings__DefaultConnection=Host=...
Embedding__ApiKey=sk-...
Embedding__BaseUrl=https://api.openai.com/v1
Embedding__Model=text-embedding-3-small
Enrichment__ApiKey=sk-...
Enrichment__Model=gpt-4o-mini
AndyAuth__Authority=https://auth.example.com
Rbac__BaseUrl=https://rbac.example.com
Sync__Enabled=true
Sync__IntervalSeconds=1800
Indexing__WorkerCount=2
Chunking__Size=1500
```
