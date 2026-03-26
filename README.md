# Andy.CodeIndex

Semantic code indexing service for the Andy ecosystem, ported from [Kodit](https://github.com/helixml/kodit) to .NET 8.

## What it does

Andy.CodeIndex indexes Git repositories for AI-powered code search and understanding:

- **Clones and indexes** repositories (GitHub, GitLab, Gitea, Azure DevOps)
- **Chunks code** into embeddable snippets with overlap (3-tier algorithm)
- **Generates embeddings** via OpenAI or any compatible API (Ollama, Azure OpenAI)
- **Hybrid search** combining vector similarity (pgvector) and BM25 keyword matching via Reciprocal Rank Fusion
- **AST-based API documentation** for C#, TypeScript, Python, Go, Java, JavaScript
- **LLM-powered enrichments**: architecture docs, API docs, wiki, cookbook, commit descriptions, dependency extraction
- **RAG chat** with intent detection for asking questions about indexed code
- **19 MCP tools** for Claude Desktop, Cursor, and other AI assistants
- **Background indexing pipeline** with task chaining, incremental updates, and quality scoring
- **Repository discovery** for GitHub and Azure DevOps organizations

## Architecture

```
Andy.CodeIndex.sln
  src/
    Andy.CodeIndex.Api            ASP.NET Core Web API + MCP server
    Andy.CodeIndex.Application    Service interfaces, DTOs, options
    Andy.CodeIndex.Domain         Entities and enums
    Andy.CodeIndex.Infrastructure EF Core, repositories, services, handlers
    Andy.CodeIndex.Shared         Shared models for API and frontend
  tests/
    Andy.CodeIndex.Tests.Unit         327 unit tests
    Andy.CodeIndex.Tests.Integration  58 integration tests
  tools/
    Andy.CodeIndex.Cli            Command-line tool
  client/                         Angular 20 frontend (61 tests)
  docs/
    requirements.md               Functional and non-functional requirements
    design.md                     Architecture, domain model, API, search
    implementation.md             Phase-by-phase implementation guide
    security.md                   Authentication, RBAC, permissions, MCP security
```

## Quick start

### Prerequisites

- .NET 8 SDK
- Docker (for PostgreSQL with pgvector)
- Node.js 20+ (for Angular frontend)

### Start the database

```bash
docker compose up postgres -d
```

### Start the backend

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Andy.CodeIndex.Api --urls "https://localhost:5101"
```

### Start the frontend

```bash
cd client && npm install && npx ng serve --proxy-config proxy.conf.json --port 4201 --ssl
```

Open https://localhost:4201 in your browser.

### Add a repository

```bash
curl -sk -X POST https://localhost:5101/api/v1/repositories \
  -H "Content-Type: application/json" \
  -d '{"url":"https://github.com/rivoli-ai/andy-code-index"}'
```

The indexing pipeline runs automatically: Clone, Sync, Scan, Extract Snippets, Extract Dependencies, Extract Commit History, BM25 Index, Code Embeddings, Summaries, API Docs, Architecture Docs, DB Schema, Commit Descriptions, Cookbook, Wiki.

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/repositories` | List repositories |
| POST | `/api/v1/repositories` | Add repository |
| GET | `/api/v1/repositories/{id}` | Repository detail with stats |
| DELETE | `/api/v1/repositories/{id}` | Delete repository |
| POST | `/api/v1/repositories/{id}/sync` | Trigger sync (409 if already active) |
| GET | `/api/v1/repositories/{id}/history` | Indexing history |
| GET | `/api/v1/repositories/{id}/commits` | List commits |
| GET | `/api/v1/repositories/{id}/blob/{ref}/{path}` | Read file |
| POST | `/api/v1/search` | Hybrid search (RRF) |
| GET | `/api/v1/search/semantic` | Semantic search |
| GET | `/api/v1/search/keyword` | Keyword search (BM25) |
| GET | `/api/v1/search/filters` | Available repos and languages |
| GET | `/api/v1/enrichments` | Query enrichments |
| GET | `/api/v1/enrichments/{id}` | Enrichment detail |
| GET | `/api/v1/enrichments/counts` | Per-subtype counts |
| GET | `/api/v1/queue` | Task queue |
| GET | `/api/v1/queue/pipelines` | Active pipeline progress |
| POST | `/api/v1/chat` | RAG chat with indexed code |
| GET | `/api/v1/chat/conversations` | List user's conversations |
| GET | `/api/v1/chat/conversations/{id}` | Get conversation with messages |
| DELETE | `/api/v1/chat/conversations/{id}` | Delete a conversation |
| PUT | `/api/v1/chat/conversations/{id}/title` | Rename a conversation |
| GET | `/api/v1/discover/{provider}` | Discover repos in an org |
| POST | `/api/v1/discover/sync` | Import discovered repos |
| GET | `/api/v1/sync/status` | Sync schedule |
| GET | `/api/v1/settings` | User settings |
| PUT | `/api/v1/settings` | Update settings |
| GET | `/health` | Health check |

Swagger UI available at `/swagger` in development mode.

## MCP Tools

29 tools exposed at `/mcp` with the `code_index_` prefix:

**Query:** `code_index_version`, `code_index_repositories`, `code_index_semantic_search`, `code_index_keyword_search`, `code_index_grep`, `code_index_read_resource`, `code_index_ls`, `code_index_chat`, `code_index_search_filters`, `code_index_commits`

**Enrichments:** `code_index_architecture_docs`, `code_index_api_docs`, `code_index_database_schema`, `code_index_cookbook`, `code_index_wiki`, `code_index_wiki_page`, `code_index_commit_description`, `code_index_commit_history`, `code_index_dependencies`, `code_index_ownership`, `code_index_security`, `code_index_operations`, `code_index_quality`, `code_index_enrichment_counts`

**Management:** `code_index_add_repository`, `code_index_delete_repository`, `code_index_sync_repository`, `code_index_analytics`, `code_index_sync_status`

## Configuration

All settings configurable via `appsettings.json` or environment variables:

```bash
# Database
ConnectionStrings__DefaultConnection="Host=localhost;Port=5436;Database=andy_code_index;..."

# Embedding (OpenAI or compatible)
Embedding__ApiKey=sk-...
Embedding__Model=text-embedding-3-small
Embedding__BaseUrl=https://api.openai.com/v1

# Authentication (Andy.Auth)
AndyAuth__Authority=https://auth.example.com

# RBAC (Andy.Rbac)
Rbac__ApiBaseUrl=https://rbac.example.com
```

## Authentication & Authorization

- **Authentication**: OAuth 2.0 with PKCE via Andy.Auth (JWT Bearer tokens)
- **RBAC**: 35 permission checks across all controllers via Andy.Rbac.Client with 5-minute in-memory cache
- **Permissions**: 9 permissions across 5 resource types (repository, search, enrichment, task, settings)
- **MCP**: OAuth Protected Resource Metadata at `/.well-known/oauth-protected-resource`
- **User API keys**: per-user embedding and LLM keys stored encrypted, resolved via 4-tier chain
- **Development fallback**: anonymous access when `AndyAuth:Authority` is empty

See [docs/security.md](docs/security.md) for the full security reference.

## Testing

```bash
# Backend (327 unit + 58 integration = 385 tests)
dotnet test

# Frontend (61 tests)
cd client && npx ng test --watch=false --browsers=ChromeHeadless
```

Coverage: Domain 92.5%, Application 86.3%, Api 67.5%, Infrastructure 31.3% (handlers for LLM and git operations are integration-tested manually). Excluding auto-generated migrations.

## Docker

```bash
# Build image (383MB)
docker build -t andy-code-index .

# Run with compose
docker compose up
```

## License

MIT
