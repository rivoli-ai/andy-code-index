# Andy.CodeIndex — Design Document

## 1. Architecture Overview

Andy.CodeIndex follows the clean architecture pattern established by the Andy ecosystem (andy-docs, andy-auth, andy-rbac), with four primary layers and clear dependency rules.

```
┌─────────────────────────────────────────────────────────────┐
│                        Clients                               │
│  Angular Frontend │ CLI Tool │ MCP Clients │ REST API Clients │
└────────┬──────────┴─────┬────┴──────┬──────┴────────┬────────┘
         │                │           │               │
┌────────▼────────────────▼───────────▼───────────────▼────────┐
│                    Andy.CodeIndex.Api                          │
│  Controllers │ MCP Tools │ Swagger │ Auth Middleware │ CORS    │
│  Program.cs: DI registration, middleware pipeline              │
└────────┬──────────────────────────────────────────────────────┘
         │ depends on
┌────────▼──────────────────────────────────────────────────────┐
│                 Andy.CodeIndex.Application                     │
│  Service Interfaces │ DTOs │ Options │ Enums                   │
└────────┬──────────────────────────────────────────────────────┘
         │ depends on
┌────────▼──────────────────────────────────────────────────────┐
│                 Andy.CodeIndex.Infrastructure                  │
│  EF Core DbContext │ Repositories │ Git Service │ Embedding    │
│  Chunking │ Enrichment │ Code Analysis │ Background Worker     │
└────────┬──────────────────────────────────────────────────────┘
         │ depends on
┌────────▼──────────────────────────────────────────────────────┐
│                   Andy.CodeIndex.Domain                        │
│  Entities │ Enums │ Value Objects │ No external dependencies   │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│                   Andy.CodeIndex.Shared                        │
│  DTOs shared between Api and Angular client                    │
│  Referenced by Api (and optionally by client via code gen)     │
└───────────────────────────────────────────────────────────────┘
```

### Dependency Rules

- **Domain** → no project dependencies (only .NET BCL)
- **Application** → Domain
- **Infrastructure** → Domain, Application
- **Api** → Application, Infrastructure, Shared
- **Shared** → no project dependencies (only .NET BCL)
- **Cli** → Shared (HTTP client to API)
- **Tests.Unit** → all projects (for testing)
- **Tests.Integration** → Api (WebApplicationFactory)

## 2. Solution Structure

```
Andy.CodeIndex.sln
├── src/
│   ├── Andy.CodeIndex.Api/                    # ASP.NET Core Web API
│   │   ├── Controllers/
│   │   │   ├── RepositoriesController.cs
│   │   │   ├── SearchController.cs
│   │   │   ├── EnrichmentsController.cs
│   │   │   └── QueueController.cs
│   │   ├── Mcp/
│   │   │   └── CodeIndexTools.cs              # [McpServerToolType] tools
│   │   └── Program.cs
│   │
│   ├── Andy.CodeIndex.Application/            # Interfaces & DTOs
│   │   ├── Interfaces/
│   │   │   ├── IRepositoryService.cs
│   │   │   ├── ISearchService.cs
│   │   │   ├── IGitService.cs
│   │   │   ├── IChunkingService.cs
│   │   │   ├── IEmbeddingService.cs
│   │   │   ├── IEmbeddingProvider.cs
│   │   │   ├── IEnrichmentService.cs
│   │   │   ├── ICodeAnalysisService.cs
│   │   │   ├── ITaskQueue.cs
│   │   │   ├── IVectorStore.cs
│   │   │   └── IRankFusionService.cs
│   │   ├── DTOs/
│   │   └── Options/
│   │       ├── EmbeddingOptions.cs
│   │       ├── ChunkingOptions.cs
│   │       ├── SyncOptions.cs
│   │       └── IndexingOptions.cs
│   │
│   ├── Andy.CodeIndex.Domain/                 # Entities & enums
│   │   ├── Entities/
│   │   │   ├── Repository.cs
│   │   │   ├── Commit.cs
│   │   │   ├── Branch.cs
│   │   │   ├── Tag.cs
│   │   │   ├── RepositoryFile.cs
│   │   │   ├── Enrichment.cs
│   │   │   ├── ContentEmbedding.cs
│   │   │   ├── IndexingTask.cs
│   │   │   └── ChunkLineRange.cs
│   │   └── Enums/
│   │       ├── EnrichmentType.cs
│   │       ├── EnrichmentSubtype.cs
│   │       ├── TaskOperation.cs
│   │       ├── TaskStatus.cs
│   │       ├── IndexType.cs
│   │       └── GitProvider.cs
│   │
│   ├── Andy.CodeIndex.Infrastructure/         # Implementations
│   │   ├── Data/
│   │   │   ├── CodeIndexDbContext.cs
│   │   │   ├── Configurations/                # IEntityTypeConfiguration<T>
│   │   │   └── Migrations/
│   │   ├── Repositories/
│   │   ├── Services/
│   │   │   ├── RepositoryService.cs
│   │   │   ├── GitService.cs
│   │   │   ├── ChunkingService.cs
│   │   │   ├── EmbeddingService.cs
│   │   │   ├── SearchService.cs
│   │   │   ├── EnrichmentService.cs
│   │   │   ├── CodeAnalysisService.cs
│   │   │   ├── RankFusionService.cs
│   │   │   └── VectorStore.cs
│   │   ├── Providers/
│   │   │   └── OpenAiEmbeddingProvider.cs
│   │   ├── Workers/
│   │   │   ├── BackgroundWorkerService.cs
│   │   │   ├── PeriodicSyncService.cs
│   │   │   └── Handlers/
│   │   │       ├── CloneRepositoryHandler.cs
│   │   │       ├── SyncRepositoryHandler.cs
│   │   │       ├── ScanCommitHandler.cs
│   │   │       ├── ExtractSnippetsHandler.cs
│   │   │       ├── CreateBM25IndexHandler.cs
│   │   │       ├── CreateCodeEmbeddingsHandler.cs
│   │   │       ├── CreateSummaryEmbeddingsHandler.cs
│   │   │       ├── CreatePublicAPIDocsHandler.cs
│   │   │       └── ... (LLM enrichment handlers)
│   │   └── CodeAnalysis/
│   │       ├── CSharpAnalyzer.cs              # Roslyn-based
│   │       ├── TypeScriptAnalyzer.cs
│   │       ├── PythonAnalyzer.cs
│   │       └── GoAnalyzer.cs
│   │
│   └── Andy.CodeIndex.Shared/                 # Shared DTOs
│       ├── RepositoryDto.cs
│       ├── SearchResultDto.cs
│       ├── EnrichmentDto.cs
│       └── IndexingTaskDto.cs
│
├── tests/
│   ├── Andy.CodeIndex.Tests.Unit/
│   └── Andy.CodeIndex.Tests.Integration/
│
├── tools/
│   └── Andy.CodeIndex.Cli/                    # CLI tool
│
├── client/                                     # Angular frontend
│   └── src/app/
│       ├── components/
│       │   ├── repositories/
│       │   ├── search/
│       │   ├── enrichments/
│       │   ├── tasks/
│       │   └── shared/
│       ├── services/
│       ├── guards/
│       ├── interceptors/
│       └── models/
│
├── docker-compose.yml
├── Dockerfile
└── docs/
    ├── requirements.md
    ├── design.md
    └── implementation.md
```

## 3. Domain Model

### 3.1 Entity Relationship Diagram

```
┌──────────────┐       ┌──────────────┐
│  Repository   │──1:N──│    Branch     │
│              │       └──────────────┘
│  Id (Guid)   │
│  Name        │       ┌──────────────┐
│  Url         │──1:N──│     Tag       │
│  CloneUrl    │       └──────────────┘
│  Provider    │
│  DefaultBranch│      ┌──────────────┐       ┌────────────────┐
│  PAT (enc)   │──1:N──│   Commit      │──1:N──│ RepositoryFile │
│  LastSyncedAt│       │              │       └────────────────┘
│  Status      │       │  Sha         │
│  CreatedAt   │       │  Message     │       ┌──────────────────┐
│  UpdatedAt   │       │  AuthorName  │──1:N──│   Enrichment      │
└──────┬───────┘       │  AuthorEmail │       │                  │
       │               │  CommittedAt │       │  Id (Guid)       │
       │               │  IsIndexed   │       │  Type (enum)     │
       │               └──────────────┘       │  Subtype (enum)  │
       │                                      │  Title           │
       └───────────────────────────1:N────────│  Content         │
                                              │  FilePath        │
                                              │  StartLine       │
                                              │  EndLine         │
                                              │  Language        │
                                              │  SearchVector    │◄─ tsvector
                                              └────────┬─────────┘
                                                       │
                                              ┌────────▼─────────┐
                                              │ ContentEmbedding  │
                                              │                  │
                                              │  Id (Guid)       │
                                              │  EmbeddingVector │◄─ pgvector
                                              │  IndexType       │   (Code/Text)
                                              └──────────────────┘

┌──────────────────┐
│  IndexingTask     │
│                  │
│  Id (Guid)       │
│  RepositoryId    │
│  CommitId        │
│  Operation (enum)│
│  Status (enum)   │
│  Progress (int)  │
│  ErrorMessage    │
│  ChainId (Guid?) │
│  CreatedAt       │
│  StartedAt       │
│  CompletedAt     │
└──────────────────┘
```

### 3.2 Enumerations

```csharp
enum EnrichmentType { Architecture, Development, History, Usage }

enum EnrichmentSubtype {
    // Architecture
    Physical, DatabaseSchema,
    // Development
    Chunk, Snippet, SnippetSummary, Example, ExampleSummary,
    // History
    CommitDescription,
    // Usage
    Cookbook, APIDocs, Wiki
}

enum TaskOperation {
    CloneRepository, SyncRepository, DeleteRepository,
    ScanCommit, RescanCommit, ExtractSnippets,
    CreateBM25Index, CreateCodeEmbeddings,
    CreateSummaryEnrichments, CreateSummaryEmbeddings,
    CreatePublicAPIDocs,
    CreateArchitectureDocs, CreateDatabaseSchema,
    CreateCommitDescription, CreateCookbook, CreateWiki
}

enum TaskStatus { Pending, Running, Completed, Failed, Cancelled }
enum IndexType { Code, Text }
enum GitProvider { GitHub, GitLab, Gitea, AzureDevOps }
```

## 4. API Design

### 4.1 REST Endpoints

Base path: `/api/v1`

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/repositories` | List repositories (filter by provider, status) | `code-index:repository:read` |
| POST | `/repositories` | Add repository | `code-index:repository:write` |
| GET | `/repositories/{id}` | Repository detail with stats | `code-index:repository:read` + instance |
| DELETE | `/repositories/{id}` | Delete repository | `code-index:repository:delete` + instance |
| POST | `/repositories/{id}/sync` | Trigger sync (409 if active tasks) | `code-index:repository:index` + instance |
| GET | `/repositories/{id}/commits` | List commits | `code-index:repository:read` + instance |
| GET | `/repositories/{id}/commits/{sha}` | Commit detail | `code-index:repository:read` + instance |
| GET | `/repositories/{id}/blob/{ref}/{**path}` | Read file (ref=branch/tag/SHA) | `code-index:repository:read` + instance |
| POST | `/search` | Hybrid search (RRF) | `code-index:search:read` |
| GET | `/search/semantic` | Semantic search | `code-index:search:read` |
| GET | `/search/keyword` | Keyword search (BM25) | `code-index:search:read` |
| GET | `/search/filters` | Available repos and languages | `code-index:search:read` |
| GET | `/enrichments` | Query enrichments (filter by type, subtype, repo) | `code-index:enrichment:read` |
| GET | `/enrichments/{id}` | Enrichment detail | `code-index:enrichment:read` |
| GET | `/enrichments/counts` | Per-subtype enrichment counts | `code-index:enrichment:read` |
| GET | `/queue` | Task queue status | `code-index:task:read` |
| GET | `/queue/{id}` | Task detail | `code-index:task:read` |
| GET | `/queue/pipelines` | Active pipeline progress per repo | `code-index:task:read` |
| POST | `/chat` | RAG chat with indexed code | `code-index:search:read` |
| GET | `/discover/{provider}` | Discover repos in GitHub/Azure org | `code-index:repository:read` |
| POST | `/discover/sync` | Import discovered repositories | `code-index:repository:write` |
| GET | `/settings` | User settings (API keys masked) | Authenticated |
| PUT | `/settings` | Update user settings | Authenticated |
| GET | `/sync/status` | Periodic sync schedule | `code-index:task:read` |
| GET | `/health` | Health check | Anonymous |

### 4.2 MCP Endpoint

- **Path:** `/mcp` (HTTP transport)
- **Auth:** JWT Bearer via Andy.Auth, MCP authentication scheme
- **CORS:** AllowMcpClients policy (any origin)
- **Metadata:** `/.well-known/oauth-protected-resource` (RFC 8707)
- **Tools:** 58 tools with `code_index_` prefix grouped into Query, Enrichments, Management, and Discovery (see README §MCP Tools), including chat, analytics, dependencies, commit history, and sync status

### 4.3 Swagger/OpenAPI

- Swashbuckle.AspNetCore with Bearer security scheme
- All endpoints documented with request/response examples
- Available at `/swagger` in Development environment

## 5. Search Architecture

### 5.1 Dual Vector Index

```
                    ┌─────────────┐
    User Query ────►│ Embed Query  │
                    └──────┬──────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
    ┌─────────────┐ ┌───────────┐ ┌──────────┐
    │ Code Vector  │ │Text Vector│ │  BM25    │
    │   Search     │ │  Search   │ │ Keyword  │
    │ (pgvector)   │ │(pgvector) │ │(tsvector)│
    └──────┬──────┘ └─────┬─────┘ └────┬─────┘
           │              │             │
           └──────────────┼─────────────┘
                          ▼
              ┌───────────────────────┐
              │ Reciprocal Rank Fusion │
              │ score = Σ 1/(k+rank)   │
              │ k=60, rank is 0-based  │
              └───────────┬───────────┘
                          ▼
              ┌───────────────────────┐
              │   Merged Results      │
              │  (sorted by fused     │
              │   score, deduplicated) │
              └───────────────────────┘
```

### 5.2 Search Filters

All three search methods support the same filter set:

```csharp
class SearchFilter {
    List<string>? Languages;          // File extensions
    List<Guid>? RepositoryIds;        // Restrict to repos
    List<string>? Authors;            // Commit authors
    DateTime? CreatedAfter;           // Date range
    DateTime? CreatedBefore;
    List<string>? FilePaths;          // Glob patterns
    string? CommitSha;                // Specific commit
    List<EnrichmentType>? Types;
    List<EnrichmentSubtype>? Subtypes;
}
```

## 6. Indexing Pipeline

### 6.1 Task Chain — Full Index

```
┌─────────────┐    ┌──────────────┐    ┌────────────┐
│ Clone Repo   │───►│ Sync Repo    │───►│ Scan Commit │
└─────────────┘    └──────────────┘    └──────┬─────┘
                                              │
    ┌─────────────────────────────────────────┘
    ▼
┌──────────────────┐    ┌──────────────────┐
│ Extract Snippets  │───►│ Create BM25 Index │
│ (merge: add/update│    └────────┬─────────┘
│  /delete chunks)  │
└──────────────────┘             │
    ┌────────────────────────────┘
    ▼
┌──────────────────────┐    ┌─────────────────────────────┐
│ Create Code Embeddings│───►│ Create Summary Enrichments   │
└──────────────────────┘    │ (LLM summarizes snippets)    │
                            └──────────────┬──────────────┘
                                           │
    ┌──────────────────────────────────────┘
    ▼
┌────────────────────────┐
│ Create Summary Embeddings│
└────────────┬───────────┘
             │
    ┌────────┘
    ▼
┌──────────────────┐    ┌───────────────────────────┐
│ Create API Docs   │    │ LLM Enrichments (optional) │
│ (AST, no LLM)    │    │ Architecture, DB Schema,   │
└──────────────────┘    │ Cookbook, Wiki, Commit Desc │
                        └───────────────────────────┘
```

### 6.2 Incremental Snippet Extraction

On re-index, ExtractSnippetsHandler performs a **merge** instead of delete-and-recreate:

1. Build new chunks from current file state
2. Load existing chunk enrichments from DB
3. Match by key: `(filePath, startLine, endLine)`
4. For each new chunk:
   - **Unchanged** (same content hash): skip — preserves ID and attached embeddings
   - **Modified** (different content hash): update content in-place — preserves ID
   - **New** (no matching key): insert new enrichment
5. For each existing chunk with no match in new set: **delete** (file removed or restructured)

Content identity is determined by SHA-256 hash of the chunk content (first 8 bytes).

### 6.3 Chunking Algorithm (Ported from Kodit)

Three-tier fixed-size chunking with overlap:

```
Parameters: Size=1500 runes, Overlap=200 runes, MinSize=50 runes
(Runes = Unicode code points. In .NET, use StringInfo or enumerate Rune for accuracy.)

Tier 1: Accumulate whole lines until next line exceeds Size
         ┌──────────────────────────┐
         │ line 1                   │
         │ line 2                   │  ◄── chunk boundary when
         │ line 3                   │      adding line 4 would
         │ ...                      │      exceed 1500 chars
         └──────────────────────────┘

Tier 2: For lines > Size, split on whitespace boundaries
         ┌──────────────────────────┐
         │ very long line split at  │  ◄── split at nearest
         │ whitespace boundary      │      whitespace ≤ Size
         └──────────────────────────┘

Tier 3: For tokens > Size with no whitespace, split on chars
         ┌──────────────────────────┐
         │ [exactly Size characters] │  ◄── hard split
         └──────────────────────────┘

Overlap: Trailing whole lines from chunk N (up to ~200 runes) become prefix of chunk N+1
         (line-granular overlap, not character-level — walks backward through lines)
```

Each chunk tracks: content, byte offset, 1-based start/end line numbers.

## 7. Authentication & Authorization

See [docs/security.md](security.md) for the full security reference covering:

- OAuth 2.0 with PKCE authentication flow (Andy.Auth)
- RBAC permission model with 9 permissions across 5 resource types
- Controller permission mapping (35 `[RequirePermission]` attributes)
- Permission caching (5-minute in-memory TTL via Andy.Rbac.Client)
- Per-user API key encryption and 4-tier resolution chain
- MCP OAuth Protected Resource Metadata (RFC 8707)
- Development setup for Andy.Auth and RBAC

## 8. External Integrations

### 8.1 Andy Ecosystem

| Service | Integration | Package/Client |
|---------|------------|----------------|
| Andy.Auth | JWT validation, OAuth flow | `Andy.Auth` NuGet (AddAndyAuth) |
| Andy.RBAC | Permission checks | `Andy.Rbac.Client` NuGet (IRbacClient) |
| Andy.Llm | LLM text generation for enrichments | `Andy.Llm` NuGet (ILlmService) |

### 8.2 Embedding Providers

| Provider | Endpoint | Models |
|----------|----------|--------|
| OpenAI | `https://api.openai.com/v1` | text-embedding-3-small (1536d), text-embedding-3-large (3072d) |
| Ollama | `http://localhost:11434/v1` | Any compatible model |
| Azure OpenAI | Custom endpoint | text-embedding-3-small/large |
| Custom | Any OpenAI-compatible | Configurable |

### 8.3 Database

- **PostgreSQL 16+** with `pgvector` extension for vector operations
- **pgvector column type** for ContentEmbedding.EmbeddingVector
- **tsvector + GIN index** for BM25 full-text search
- **HNSW index** for vector similarity search performance

## 9. Configuration

### 9.1 appsettings.json Structure

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=andy_code_index;Username=..."
  },
  "AndyAuth": {
    "Authority": "https://auth.example.com",
    "Audience": "urn:andy-code-index-api",
    "AuthProvider": "AndyAuth"
  },
  "Rbac": {
    "ApiBaseUrl": "https://rbac.example.com",
    "ApplicationCode": "code-index"
  },
  "Embedding": {
    "BaseUrl": "https://api.openai.com/v1",
    "Model": "text-embedding-3-small",
    "ApiKey": "",  // from environment/secrets
    "MaxBatchSize": 1,
    "MaxBatchChars": 16000,
    "Timeout": 60,
    "MaxRetries": 5,
    "Parallelism": 1
  },
  "Enrichment": {
    "BaseUrl": "https://api.openai.com/v1",
    "Model": "gpt-4o-mini",
    "ApiKey": "",
    "Parallelism": 1
  },
  "Chunking": {
    "Size": 1500,
    "Overlap": 200,
    "MinSize": 50
  },
  "Sync": {
    "Enabled": true,
    "IntervalSeconds": 1800
  },
  "Indexing": {
    "DataDir": "~/.andy-code-index",
    "WorkerCount": 1,
    "SearchLimit": 10
  },
  "AllowedHosts": "*"
}
```

## 10. Deployment Architecture

### 10.1 Docker Compose (Development)

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg16
    ports: [7436:5432]
    volumes: [postgres_data:/var/lib/postgresql/data]

  api:
    build: .
    ports:
      - "7101:8443"  # HTTPS
      - "7102:8080"  # HTTP
      - "6201:8443"  # Docker client alias
    depends_on: [postgres]
    volumes:
      - code_index_data:/data         # clone directory
    environment:
      - ConnectionStrings__DefaultConnection=...
      - Embedding__ApiKey=...

  ollama:  # optional, for local embeddings (profile: ollama)
    image: ollama/ollama
    ports: [11434:11434]
```

### 10.2 Production Considerations

- Container runs as non-root user
- Health check at `/health`
- Secrets via environment variables (never in appsettings)
- HTTPS enforced via reverse proxy
- Data directory mounted as persistent volume
- PostgreSQL with connection pooling (PgBouncer)

## 11. Testing Strategy

### 11.1 Test Pyramid

```
          ┌───────────────┐
          │  Integration   │  WebApplicationFactory, real DB
          │  Tests         │  End-to-end API + MCP tests
          ├───────────────┤
          │  Unit Tests    │  In-memory DB, mocked services
          │                │  All services, controllers, handlers
          ├───────────────┤
          │  Frontend      │  Jasmine + Karma (Angular TestBed)
          │  Tests         │  Components + services
          └───────────────┘
```

### 11.2 Backend Testing Patterns (matching andy-docs)

```csharp
// Unit test pattern
public class RepositoriesControllerTests : IDisposable
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IRepositoryService> _mockService;
    private readonly RepositoriesController _controller;

    public RepositoriesControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new CodeIndexDbContext(options);
        _mockService = new Mock<IRepositoryService>();
        _controller = new RepositoriesController(_mockService.Object);
    }

    [Fact]
    public async Task GetRepositories_ReturnsOkWithList() { ... }

    public void Dispose() => _context.Dispose();
}

// Integration test pattern
public class RepositoryApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    [Fact]
    public async Task CreateRepository_ReturnsCreated() { ... }
}
```

### 11.3 Frontend Testing Pattern

```typescript
// Component test
describe('RepositoryListComponent', () => {
  let component: RepositoryListComponent;
  let fixture: ComponentFixture<RepositoryListComponent>;
  let apiService: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiService = jasmine.createSpyObj('ApiService', ['getRepositories']);
    await TestBed.configureTestingModule({
      imports: [RepositoryListComponent],
      providers: [{ provide: ApiService, useValue: apiService }]
    }).compileComponents();
  });

  it('should display repositories', () => { ... });
});
```

### 11.4 Coverage Targets

| Layer | Target | Tool |
|-------|--------|------|
| Backend services | ≥ 85% | coverlet + dotnet test |
| Backend controllers | ≥ 90% | coverlet |
| Frontend components | ≥ 80% | karma-coverage |
| Frontend services | ≥ 85% | karma-coverage |
