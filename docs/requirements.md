# Andy.CodeIndex — Requirements

## 1. Overview

Andy.CodeIndex is a semantic code indexing service for the rivoli-ai ecosystem, ported from [Kodit](https://github.com/helixml/kodit) (Go) to .NET 10. It provides AI-powered code understanding, hybrid search, and MCP tool access for agentic workflows.

## 2. Goals

1. **Semantic Code Search** — Index repositories and enable natural language code search via vector embeddings and BM25 keyword matching.
2. **MCP Integration** — Expose all capabilities as MCP tools for Claude Desktop, Cursor, Windsurf, and other AI assistants.
3. **Andy Ecosystem Fit** — Follow the architecture, auth, RBAC, and deployment patterns established by andy-docs, andy-auth, and andy-rbac.
4. **Multi-Repository Intelligence** — Index all rivoli-ai repositories and support cross-repository search and navigation.
5. **Comprehensive Testing** — Unit and integration tests for both backend and frontend with ≥85% code coverage.

## 3. Functional Requirements

### 3.1 Repository Management (Epic #15)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Add repositories by URL (GitHub, GitLab, Gitea, Azure DevOps) | Must |
| FR-02 | Support private repositories via Personal Access Token | Must |
| FR-03 | Clone repositories to local storage | Must |
| FR-04 | Sync repositories (fetch latest commits, branches, tags) | Must |
| FR-05 | Delete repositories with cascade cleanup (DB, embeddings, clone dir) | Must |
| FR-06 | List all tracked repositories with status | Must |
| FR-07 | Periodic automatic sync at configurable interval (default 30 min) | Must |
| FR-08 | Selective reindexing — only process changed files | Should |
| FR-09 | Respect `.gitignore` and `.noindex` files | Must |
| FR-10 | GitHub organization repository auto-discovery | Should |

### 3.2 Code Indexing Pipeline (Epic #30)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-11 | Chunk code files using fixed-size overlap algorithm (1500 runes, 200 overlap, 50 min; line-granular overlap) | Must |
| FR-12 | Generate vector embeddings for code chunks (code index) | Must |
| FR-13 | Generate vector embeddings for enrichment summaries (text index) | Must |
| FR-14 | Create BM25 full-text index using PostgreSQL tsvector | Must |
| FR-15 | Background task queue with database-backed persistence | Must |
| FR-16 | Task chaining: clone → sync → scan → chunk → BM25 → code-embed → summary-enrich → summary-embed → API docs → LLM enrichments | Must |
| FR-17 | Progress reporting for long-running operations | Must |
| FR-18 | Configurable worker count for concurrent processing | Should |
| FR-19 | Support multiple embedding providers (OpenAI, Ollama, compatible APIs) | Must |
| FR-20 | Token budget management to prevent exceeding model limits | Must |
| FR-21 | Retry with exponential backoff for transient failures | Must |

### 3.3 LLM-Powered Enrichments (Epic #30, Feature #34)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-22 | Architecture documentation — high-level system overview | Should |
| FR-23 | Database schema documentation | Should |
| FR-24 | Commit descriptions — AI-generated context beyond commit message | Should |
| FR-25 | Cookbook — usage examples and patterns | Should |
| FR-26 | Wiki — generated documentation with table of contents and pages | Should |
| FR-27 | AST-based public API documentation (no LLM required) | Must |
| FR-28 | Support C#, TypeScript, Python, Go for AST analysis (minimum) | Must |
| FR-29 | Cascade-delete old enrichments when regenerating | Must |
| FR-30 | Enrichments searchable via text vector search | Must |

### 3.4 Hybrid Search (Epic #55)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-31 | Semantic search via pgvector cosine similarity | Must |
| FR-32 | BM25 keyword search via PostgreSQL full-text | Must |
| FR-33 | Hybrid search combining both via Reciprocal Rank Fusion | Must |
| FR-34 | Search filters: language, repository, author, date range, file path, commit SHA | Must |
| FR-35 | Configurable result limit (default 10) | Must |
| FR-36 | File listing by glob pattern | Must |
| FR-37 | Git grep with regex pattern support | Must |
| FR-38 | Cross-repository search (no repo filter = all repos) | Must |

### 3.5 MCP Server (Epic #69)

All 14 Kodit MCP tools must be ported with `code_index_` prefix:

| ID | Tool | Parameters | Priority |
|----|------|-----------|----------|
| FR-39 | `code_index_version` | (none) | Must |
| FR-40 | `code_index_repositories` | (none) | Must |
| FR-41 | `code_index_architecture_docs` | repo_url (req), commit_sha (opt) | Must |
| FR-42 | `code_index_api_docs` | repo_url (req), commit_sha (opt) | Must |
| FR-43 | `code_index_commit_description` | repo_url (req), commit_sha (opt) | Must |
| FR-44 | `code_index_database_schema` | repo_url (req), commit_sha (opt) | Must |
| FR-45 | `code_index_cookbook` | repo_url (req), commit_sha (opt) | Must |
| FR-46 | `code_index_wiki` | repo_url (req), commit_sha (opt) | Must |
| FR-47 | `code_index_wiki_page` | repo_url (req), page_slug (req), commit_sha (opt) | Must |
| FR-48 | `code_index_semantic_search` | query (req), language (opt), source_repo (opt), limit (opt) | Must |
| FR-49 | `code_index_keyword_search` | keywords (req), source_repo (opt), language (opt), limit (opt) | Must |
| FR-50 | `code_index_grep` | repo_url (req), pattern (req), glob (opt), limit (opt) | Must |
| FR-51 | `code_index_read_resource` | uri (req) | Must |
| FR-52 | `code_index_ls` | repo_url (req), pattern (req) | Must |

### 3.6 Authentication & Authorization (Epic #79)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-53 | JWT Bearer authentication via Andy.Auth | Must |
| FR-54 | OAuth Protected Resource Metadata (RFC 8707) at `/.well-known/oauth-protected-resource` | Must |
| FR-55 | RBAC permission model: `code-index:{resource-type}:{action}` | Must |
| FR-56 | Resource types: repository (instance-level), search, enrichment, task, admin | Must |
| FR-57 | Actions: read, write, delete, index, search, manage | Must |
| FR-58 | Default roles: viewer, contributor, admin | Must |
| FR-59 | Instance-level permissions per repository | Must |
| FR-60 | MCP tools respect same permission model | Must |

### 3.7 Web Frontend (Epic #91)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-61 | Angular 20+ with standalone components | Must |
| FR-62 | OAuth 2.0 PKCE authentication with Andy.Auth | Must |
| FR-63 | Repository management: list, add, detail, sync, delete | Must |
| FR-64 | Search: semantic, keyword, hybrid modes with filters | Must |
| FR-65 | Search results with code syntax highlighting | Must |
| FR-66 | File viewer with line range highlighting | Must |
| FR-67 | Enrichment browser with type filtering | Must |
| FR-68 | Wiki viewer with table of contents navigation | Should |
| FR-69 | Task queue dashboard with progress indicators | Must |

### 3.8 CLI Tool (Epic #114)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-70 | Repository management commands (list, add, remove, sync, status) | Should |
| FR-71 | Search commands (semantic, keyword, hybrid, grep) | Should |
| FR-72 | Enrichment browsing commands | Should |
| FR-73 | Table and JSON output formats | Should |
| FR-74 | Colorized TTY output, plain for pipes | Should |

### 3.9 Ecosystem Integration (Epic #120)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-75 | Seed configuration for rivoli-ai repos | Should |
| FR-76 | GitHub organization auto-discovery | Should |
| FR-77 | MCP responses optimized for AI consumption (structured, size-managed) | Must |
| FR-78 | Cross-repository navigation via MCP tools | Must |

## 4. Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-01 | Search latency | < 500ms for typical queries |
| NFR-02 | Indexing throughput | Handle repos up to 100K files |
| NFR-03 | Code coverage (backend) | ≥ 85% line coverage |
| NFR-04 | Code coverage (frontend) | ≥ 80% line coverage |
| NFR-05 | API documentation | All endpoints in Swagger/OpenAPI |
| NFR-06 | Container image size | < 500MB |
| NFR-07 | Startup time | < 10 seconds to healthy state |
| NFR-08 | Concurrent users | Support 50+ concurrent API clients |
| NFR-09 | Database | PostgreSQL 16+ with pgvector extension |
| NFR-10 | Runtime | .NET 10 LTS |
| NFR-11 | Security | No OWASP Top 10 vulnerabilities |
| NFR-12 | Deployment | Docker container, Railway-compatible |

## 5. Constraints

- **Must use .NET 10** — the supported LTS baseline for this service.
- **Must use PostgreSQL with pgvector** — vector storage and similarity search.
- **Must integrate with Andy.Auth** — no standalone auth implementation.
- **Must integrate with Andy.RBAC** — no standalone permission system.
- **Must follow clean architecture** — Domain, Application, Infrastructure, API layers.
- **Must use ModelContextProtocol NuGet package** — same MCP library as andy-docs.
- **Angular frontend** — matching andy-docs client patterns.

## 6. Traceability Matrix

| Requirement | Epic | Feature(s) | Stories |
|-------------|------|-----------|---------|
| FR-01..FR-10 | #15 | #16, #17, #18 | #19–#29 |
| FR-11..FR-21 | #30 | #31, #32, #33 | #36–#46 |
| FR-22..FR-30 | #30 | #34, #35 | #47–#54 |
| FR-31..FR-38 | #55 | #56, #57, #58, #59 | #60–#68 |
| FR-39..FR-52 | #69 | #70, #71 | #72–#78 |
| FR-53..FR-60 | #79 | #80, #81, #82 | #83–#90 |
| FR-61..FR-69 | #91 | #92, #93, #94, #95, #96 | #97–#113 |
| FR-70..FR-74 | #114 | #115 | #116–#119 |
| FR-75..FR-78 | #120 | #121, #122 | #123–#128 |
| Enrichment API | #129 | #130 | #131–#133 |
