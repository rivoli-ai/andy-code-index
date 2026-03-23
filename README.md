# Andy.CodeIndex

Semantic code indexing service for the Andy ecosystem, ported from [Kodit](https://github.com/helixml/kodit) to .NET 8.

## What it does

Andy.CodeIndex indexes Git repositories for AI-powered code search and understanding:

- **Clones and indexes** repositories (GitHub, GitLab, Gitea, Azure DevOps)
- **Chunks code** into embeddable snippets with overlap (3-tier algorithm)
- **Generates embeddings** via OpenAI or any compatible API (Ollama, Azure OpenAI)
- **Hybrid search** combining vector similarity (pgvector) and BM25 keyword matching via Reciprocal Rank Fusion
- **AST-based API documentation** for C#, TypeScript, Python, Go, Java, JavaScript
- **14 MCP tools** for Claude Desktop, Cursor, and other AI assistants
- **Background indexing pipeline** with task chaining and incremental updates

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
    Andy.CodeIndex.Tests.Unit         252 unit tests
    Andy.CodeIndex.Tests.Integration  27 integration tests
  tools/
    Andy.CodeIndex.Cli            Command-line tool
  client/                         Angular 20 frontend (59 tests)
  docs/
    requirements.md               Functional and non-functional requirements
    design.md                     Architecture, domain model, API, search
    implementation.md             Phase-by-phase implementation guide
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

The indexing pipeline runs automatically: Clone, Sync, Scan, Extract Snippets, BM25 Index, Code Embeddings, API Docs.

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/repositories` | List repositories |
| POST | `/api/v1/repositories` | Add repository |
| GET | `/api/v1/repositories/{id}` | Repository detail |
| DELETE | `/api/v1/repositories/{id}` | Delete repository |
| POST | `/api/v1/repositories/{id}/sync` | Trigger sync |
| GET | `/api/v1/repositories/{id}/history` | Indexing history |
| GET | `/api/v1/repositories/{id}/commits` | List commits |
| GET | `/api/v1/repositories/{id}/blob/{ref}/{path}` | Read file |
| POST | `/api/v1/search` | Hybrid search |
| GET | `/api/v1/search/semantic` | Semantic search |
| GET | `/api/v1/search/keyword` | Keyword search |
| GET | `/api/v1/enrichments` | Query enrichments |
| GET | `/api/v1/enrichments/{id}` | Enrichment detail |
| GET | `/api/v1/queue` | Task queue |
| GET | `/api/v1/sync/status` | Sync schedule |
| GET | `/api/v1/settings` | User settings |
| PUT | `/api/v1/settings` | Update settings |
| GET | `/health` | Health check |

Swagger UI available at `/swagger` in development mode.

## MCP Tools

14 tools exposed at `/mcp` with the `code_index_` prefix:

`code_index_version`, `code_index_repositories`, `code_index_architecture_docs`, `code_index_api_docs`, `code_index_commit_description`, `code_index_database_schema`, `code_index_cookbook`, `code_index_wiki`, `code_index_wiki_page`, `code_index_semantic_search`, `code_index_keyword_search`, `code_index_grep`, `code_index_read_resource`, `code_index_ls`

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

## Authentication

- **Development**: anonymous access (no auth required when `AndyAuth:Authority` is empty)
- **Production**: OAuth 2.0 via Andy.Auth with JWT Bearer tokens
- **MCP**: OAuth Protected Resource Metadata at `/.well-known/oauth-protected-resource`
- **User API keys**: per-user embedding keys stored encrypted, resolved via 3-tier chain (user, system, none)

## Testing

```bash
# Backend (252 unit + 27 integration)
dotnet test

# Frontend (59 tests)
cd client && npx ng test --watch=false --browsers=ChromeHeadless
```

Coverage: 81.4% line coverage (excluding auto-generated migrations).

## Docker

```bash
# Build image (383MB)
docker build -t andy-code-index .

# Run with compose
docker compose up
```

## License

MIT
