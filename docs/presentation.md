---
marp: true
theme: default
paginate: true
size: 16:9
header: 'Andy Code Index — End-to-End Walkthrough'
footer: 'Rivoli AI · andy-code-index'
style: |
  section { font-size: 24px; }
  section h1 { color: #1f4e79; }
  section h2 { color: #2e75b6; border-bottom: 2px solid #2e75b6; padding-bottom: 4px; }
  code { background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }
  pre { font-size: 18px; }
  table { font-size: 20px; }
---

<!-- _class: lead -->
<!-- _paginate: false -->

# Andy Code Index
## End-to-End System Walkthrough

AI-powered code search and semantic understanding across Git repositories for the Andy ecosystem.

*Designed for engineers who have never seen this service before.*

---

## What is Andy Code Index?

A **semantic code search and understanding service**. Clone any Git repo (GitHub / GitLab / Gitea / Azure DevOps), chunk + embed the code, and expose fast **hybrid search** (semantic + keyword + LLM-generated docs) via REST, MCP, and a chat UI.

- Vector search (pgvector + HNSW) **and** BM25 (tsvector)
- **Reciprocal Rank Fusion** of the two
- LLM enrichments: architecture docs, API docs, cookbook, wiki
- Roslyn + tree-sitter parsers per language
- MCP tools for Claude Desktop, Cursor, ChatGPT

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 8.0 |
| API | REST + MCP |
| Frontend | Angular 20 (RxJS, PrismJS, Marked) |
| Database | **PostgreSQL 16 + pgvector + tsvector** |
| ORM | Entity Framework Core 8 |
| Embeddings | OpenAI / Ollama / Azure OpenAI |
| LLM enrichment | OpenAI (gpt-4o-mini) via Andy.Llm |
| Parsers | **Roslyn** (C#), **tree-sitter** (TS/Py/Go/Java/JS) |
| Auth | JWT Bearer (Andy Auth) + RBAC |

---

## Solution Layout

```
andy-code-index/
├── src/
│   ├── Andy.CodeIndex.Domain/          ← entities, enums
│   ├── Andy.CodeIndex.Application/     ← interfaces, options
│   ├── Andy.CodeIndex.Infrastructure/  ← EF, handlers, services
│   ├── Andy.CodeIndex.Api/             ← REST + MCP
│   └── Andy.CodeIndex.Shared/          ← DTOs for Angular
├── client/                             ← Angular 20 SPA
├── tools/Andy.CodeIndex.Cli/
└── tests/
    ├── Andy.CodeIndex.Tests.Unit/        (327 tests)
    └── Andy.CodeIndex.Tests.Integration/ (58 tests)
```

---

## Domain Model — Core Aggregates

- **Repository** — the unit of work
  - `Url`, `CloneUrl`, `Provider`, `DefaultBranch`, `PersonalAccessToken` (encrypted)
  - `LastIndexedCommitSha`, `LastSyncedAt`, `Status`
  - `SyncIntervalMinutes`, `FileFilterOverrides` (per-repo)
- **Commit** — SHA, message, author, committedAt
- **Branch**, **Tag**, **RepositoryFile** — git state
- **IndexingTask** — queued pipeline step
- **UserSettings** — per-user encrypted API keys
- **ChatConversation** + **ChatMessage** — RAG history

---

## Domain Model — Enrichments

**`Enrichment`** is the unit of indexed content:

- `Type`: Architecture / Development / History / Usage
- `Subtype` (12 values): Physical · DatabaseSchema · Chunk · Snippet · SnippetSummary · Example · ExampleSummary · CommitDescription · Cookbook · APIDocs · Wiki · ...
- `Title`, `Content`, `FilePath`, `StartLine`, `EndLine`, `Language`, `Quality`
- `SearchVector` (tsvector — auto-generated STORED column) for BM25

**`ContentEmbedding`** — pgvector column (1536 or 3072 dims) with `IndexType`: Code or Text. HNSW index for cosine similarity < 1ms.

---

## Application Interfaces

- **`IRepositoryService`** — add/list/sync/delete repos
- **`ISearchService`** — `SemanticSearchAsync`, `KeywordSearchAsync`, **`HybridSearchAsync`**
- **`IEmbeddingService`** — `GenerateEmbeddingsAsync`, `StoreEmbeddingsAsync`, `Dimensions`, `ModelName`
- **`IChunkingService`** — three-tier chunker (size 1500, overlap 200)
- **`IGitService`** — clone/pull, ls, read, grep
- **`IEnrichmentGeneratorService`** — LLM-driven doc enrichments
- **`IChatService`** — RAG chat with repo context
- **`ITaskQueue`** — background work
- **`ICodeAnalysisService`** — public API extraction

---

## The Indexing Pipeline — 18 Handlers

Task chain (from a URL to a fully indexed repo):

```
CloneRepository → Sync → ScanCommit
  → ExtractSnippets (chunker)
  → CreateBM25Index (tsvector)
  → CreateCodeEmbeddings (pgvector)
  → CreateSummaryEmbeddings
  → CreatePublicAPIDocs (Roslyn / tree-sitter)
  → CreateArchitectureDocs (LLM)
  → CreateDatabaseSchema (LLM)
  → CreateCookbook (LLM)
  → CreateWiki (LLM)
  → CreateCommitDescription (per-commit, LLM)
```

Each handler is idempotent and merges chunks by hash (preserve / update / delete).

---

## Search — Hybrid Retrieval

**Semantic search:**

```sql
SELECT * FROM ContentEmbeddings ce
JOIN Enrichments e ON ce.EnrichmentId = e.Id
ORDER BY ce.EmbeddingVector <-> $query_vector
LIMIT 10;
```

**Keyword (BM25-equivalent):**

```sql
SELECT * FROM Enrichments
WHERE SearchVector @@ plainto_tsquery('english', $q)
ORDER BY ts_rank(SearchVector, …) DESC;
```

**Rank fusion:** `RankFusionService` combines both using Reciprocal Rank Fusion (`score = 1 / (k + rank)`, `k=60`).

---

## Embeddings — Providers

`OpenAiEmbeddingProvider` (4-tier key resolution):

1. User-specific (`UserSettings.EmbeddingApiKey`)
2. Organization key (RBAC context)
3. Instance default (`EmbeddingOptions.ApiKey`)
4. Fallback embedded key

Supports:

- OpenAI (`text-embedding-3-small` 1536d / `text-embedding-3-large` 3072d)
- Ollama (local models)
- Azure OpenAI
- Any OpenAI-compatible endpoint

Batching, retries (5×), 60s timeout.

---

## REST API — 13 Controllers

| Controller | Highlights |
|-----------|-----------|
| `RepositoriesController` | add/list/sync/delete |
| `SearchController` | hybrid / semantic / keyword |
| `EnrichmentsController` | filter by type/subtype |
| `ChatController` | RAG chat + conversations |
| `QueueController` | task pipelines |
| `CommitsController` | per-commit views |
| `GitController` | ls / grep / blob |
| `DiscoveryController` | list org repos, bulk import |
| `SettingsController` | user API keys |
| `AnalyticsController` | activity stats |
| `FilesController` | file streaming |
| `InsightsController` | quality metrics |
| `IndexingController` | status |

All `[Authorize]` + `[RequirePermission("code-index:…")]`.

---

## MCP Surface — 58 Tools

`Mcp/CodeIndexTools.cs` (1358 LOC) — grouped:

**Query**: `code_index_semantic_search`, `code_index_keyword_search`, `code_index_grep`, `code_index_read_resource`, `code_index_ls`, `code_index_chat`, `code_index_commits`, `code_index_search_filters`

**Enrichments**: `code_index_architecture_docs`, `code_index_api_docs`, `code_index_database_schema`, `code_index_cookbook`, `code_index_wiki`, `code_index_commit_description`, `code_index_dependencies`, `code_index_ownership`, `code_index_security`, `code_index_quality`

**Management**: `code_index_add_repository`, `code_index_delete_repository`, `code_index_sync_repository`

OAuth Protected Resource Metadata at `/.well-known/oauth-protected-resource` (RFC 8707).

---

## Chunker — Three Tiers

`ChunkingService.ChunkText(content, filePath?, options?)`:

1. **Tier 1** — accumulate lines until `Size` (1500 runes) exceeded
2. **Tier 2** — split long lines at whitespace boundaries
3. **Tier 3** — hard-split oversized tokens
4. **Overlap** — last ~200 runes prepended to next chunk

Merge logic: same content hash → skip, different → update, missing → delete. Keeps embeddings spend under control on incremental syncs.

---

## Language Parsers

| Language | Tool |
|----------|------|
| C# | **Roslyn** |
| TypeScript | tree-sitter |
| Python | tree-sitter |
| Go | tree-sitter |
| Java, JavaScript | generic / tree-sitter |

Extract public classes, methods, interfaces, signatures → stored as `Enrichment` (Subtype: `APIDocs`). These feed both search and the LLM "architecture docs" pipeline.

---

## RAG Chat Flow

1. User sends "how do I authenticate?"
2. `QuestionClassifier` → intent = `ArchitectureQuestion`
3. `ChatFileAccessService` — checks RBAC per repo
4. `HybridSearchAsync` — top 20 enrichments
5. Build prompt: system prompt + enrichments (truncated to token limit)
6. Call **gpt-4o-mini** via Andy.Llm
7. Persist `ChatConversation` + `ChatMessage`
8. Return generated answer with cited enrichments

Conversations are per-user; titles auto-generated.

---

## Angular 20 Frontend

Standalone components + RxJS. Highlights:

- **RepositoryList** — add/delete/sync, status badges
- **Search** — hybrid UI, language + repo filter, highlighted snippets
- **EnrichmentBrowser** — filter by type/subtype
- **TaskDashboard** — live pipeline progress (polling)
- **Chat** — RAG chat with history
- **FileViewer** — syntax highlighting via PrismJS
- **WikiViewer** — markdown rendering

JWT from Andy Auth via HTTP interceptor.

---

## Consumers

- **andy-issues** — `DraftBacklogGenerator` calls `code_index_semantic_search` + `code_index_architecture_docs` to seed proposed stories from code
- **andy-agents** — pulls context for agent tasks
- **Claude Desktop / Cursor / ChatGPT** — via MCP
- **Humans** — the Angular SPA + `/swagger` UI
- Future: **andy-docs** cross-references

Everything is behind RBAC; instance-level permissions apply (per-repo access).

---

## Background Workers & Sync

- **Task queue worker** — pulls `IndexingTask` rows, runs handlers
- **Sync worker** — honours per-repo `SyncIntervalMinutes` (or global `Sync.IntervalSeconds`, default 1800)
- Workers run in `ContainerProvisioningWorker`-style BackgroundService slots
- Progress + chain state tracked in `IndexingTask` (`Progress`, `ChainId`, `ChainStepIndex`, `ChainTotalSteps`)

Full (re)index pipeline for a brand new repo takes ~5 min for small repos, scales with code size.

---

## Configuration Snapshot

```json
"ConnectionStrings": { "DefaultConnection": "Host=postgres;Port=5432;Database=andy_code_index;…" },
"AndyAuth":   { "Authority": "…", "Audience": "urn:andy-code-index-api" },
"Rbac":       { "ApiBaseUrl": "…", "ApplicationCode": "code-index" },
"Embedding":  { "BaseUrl": "https://api.openai.com/v1",
                "Model": "text-embedding-3-small",
                "ApiKey": "", "MaxBatchSize": 1,
                "MaxBatchChars": 16000, "Timeout": 60, "MaxRetries": 5 },
"Enrichment": { "Model": "gpt-4o-mini", "ApiKey": "" },
"Chunking":   { "Size": 1500, "Overlap": 200, "MinSize": 50 },
"Sync":       { "Enabled": true, "IntervalSeconds": 1800 },
"Indexing":   { "DataDir": "~/.andy-code-index", "WorkerCount": 1, "SearchLimit": 10 }
```

---

## Ports & Docker

| Port | Service |
|------|---------|
| 5101 | API HTTPS |
| 5102 | API HTTP |
| 5436 | PostgreSQL (pgvector image) |
| 11434 | Ollama (optional profile) |
| 4201 | Angular dev |

`docker-compose.yml` runs `pgvector/pgvector:pg16`, API, optional Ollama.

Multi-stage Dockerfile: Node → .NET SDK → ASP.NET runtime, non-root user `codeindex`, image size ~383 MB, `/health` check.

---

## Testing

- **Unit** (`Tests.Unit`) — 327 tests (Controllers, Services, Handlers, Workers, MCP, Helpers)
  - `HybridSearch_WithLanguageFilter_ReturnsOnlyMatchingLanguage`
  - `ChunkText_WithOversizedLine_SplitsOnWhitespace`
- **Integration** (`Tests.Integration`) — 58 tests via `WebApplicationFactory<Program>`
- **Frontend** — 61 Jasmine/Karma tests

Coverage: Domain 92%, Application 86%, Api 67%, Infrastructure 31% (LLM handlers stubbed).

---

## Data Flow — Indexing a Repo (Summary)

```
POST /api/v1/repositories  { url }
  → Repository row created (Status=pending)
  → Task chain enqueued

Clone → Sync → Scan → ExtractSnippets
     → CreateBM25Index
     → CreateCodeEmbeddings
     → CreateSummaryEmbeddings
     → CreatePublicAPIDocs
     → LLM enrichments
  → Status = indexed
```

Subsequent `sync` runs pull new commits and re-run only affected handlers.

---

## Data Flow — Searching

```
POST /api/v1/search { query, filter: {language, repoIds} }

1. Embed query (OpenAI)  ─────────┐
2. Semantic search (pgvector)     │
3. Keyword search (tsvector)   ─► 4. RRF fusion (k=60)
                                  │
                                  ▼
                               SearchResultsDto {
                                 enrichments[],
                                 totalCount, executionTimeMs
                               }
```

Returned payloads include repo, file path, line ranges, quality — enough for a client to open the exact chunk in an IDE.

---

<!-- _class: lead -->

# Where to start reading

1. `src/Andy.CodeIndex.Domain/Entities/Enrichment.cs` — the unit of content
2. `src/Andy.CodeIndex.Application/Interfaces/ISearchService.cs`
3. `src/Andy.CodeIndex.Infrastructure/Services/SearchService.cs` + `RankFusionService.cs`
4. `src/Andy.CodeIndex.Infrastructure/Handlers/ExtractSnippetsHandler.cs` — chunk merge logic
5. `src/Andy.CodeIndex.Infrastructure/Services/OpenAiEmbeddingProvider.cs`
6. `src/Andy.CodeIndex.Api/Mcp/CodeIndexTools.cs` — the 58-tool surface

Web UI: port 4201 · MCP: `/mcp` · Swagger: `/swagger`.
