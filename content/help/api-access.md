---
title: "API Access"
order: 5
tags: [api, integrations]
---

# API Access

Andy Code Index exposes a REST API for searching, managing repositories, and monitoring indexing jobs. It is designed to be consumed by clients such as the Conductor macOS app, web dashboards, CLI tools, and MCP servers.

## Authentication

All API endpoints require authentication via a Bearer token:

```http
Authorization: Bearer <api-key>
```

API keys can be generated from the Settings page or via the admin API. Keys are scoped to organizations and can be rotated without downtime.

## Rate Limits

| Endpoint Type | Limit |
|---------------|-------|
| Search | 100 requests/minute |
| Repository management | 30 requests/minute |
| Indexing status | 60 requests/minute |

Search limits are per API key and reset every minute.

## Key Endpoints

### Search

- `POST /api/v1/search` — Hybrid semantic + keyword search
- `GET /api/v1/search/semantic` — Pure semantic search
- `GET /api/v1/search/keyword` — Pure BM25 keyword search

### Repositories

- `GET /api/v1/repositories` — List indexed repositories
- `POST /api/v1/repositories` — Add a new repository
- `DELETE /api/v1/repositories/{id}` — Remove a repository

### Indexing

- `GET /api/v1/indexing/status` — Global indexing status
- `GET /api/v1/indexing/queue` — Current queue depth and jobs

### Help

- `GET /api/help/topics` — List all help topics
- `GET /api/help/topics/{slug}` — Get a single help topic
- `GET /api/help/search?q={query}` — Search help content

## Integrations

### Conductor (macOS)

The Conductor app uses the search and help endpoints to provide native semantic code search. It supports:

- Quick search from the menu bar
- Inline citations with copy-to-clipboard
- Repository filter presets

### CLI

```bash
andy search "error handling pattern" --repo my-service --limit 5
```

### MCP Server

The Model Context Protocol (MCP) server exposes Andy Code Index as a tool for LLM-based agents. Tools include:

- `semantic_code_search` — Natural language code search
- `get_file_content` — Retrieve full file content by path
- `list_repositories` — Discover available repositories

## SDKs

- **C#** — `Andy.CodeIndex.Client` NuGet package
- **TypeScript** — `andy-code-index` npm package
- **Python** — `andy-code-index` PyPI package

Each SDK handles authentication, retries, and result deserialization.

## OpenAPI / Swagger

Interactive API documentation is available at `/swagger` when running in development mode. The OpenAPI spec can be exported for generating custom clients.
