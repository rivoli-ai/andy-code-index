---
title: "Search"
order: 2
tags: [search, code]
---

# Search

The search system in Andy Code Index supports multiple search strategies, each optimized for different types of queries.

## Hybrid Search (Recommended)

The default search endpoint combines semantic and keyword results using **Reciprocal Rank Fusion (RRF)**. This produces the most robust results for general queries.

```http
POST /api/v1/search
Content-Type: application/json

{
  "query": "error handling in payment service",
  "limit": 10,
  "languages": ["csharp", "typescript"],
  "repositoryIds": ["..."]
}
```

## Semantic Search

Use semantic search when you know what you want conceptually but don't know the exact keywords or function names.

```http
GET /api/v1/search/semantic?query=retry+logic+with+exponential+backoff&limit=10
```

The query is converted into a dense vector embedding and matched against pre-computed code embeddings.

## Keyword Search (BM25)

Use keyword search when you know exact terms, function names, or class names.

```http
GET /api/v1/search/keyword?keywords=AuthService.ValidateToken&limit=10
```

BM25 scoring rewards term frequency and inverse document frequency, making it ideal for rare or specific identifiers.

## Filters

All search endpoints support the same filter dimensions:

| Filter | Description |
|--------|-------------|
| `languages` | Programming language filter (e.g., `csharp`, `python`) |
| `repositoryIds` | Scope search to specific repositories |
| `commitSha` | Search within a specific commit snapshot |
| `filePath` | Limit to files matching a path glob |

## Understanding Results

Each result includes:

- **File path** — Relative path within the repository
- **Line range** — Start and end lines of the matched chunk
- **Score** — Normalized relevance score (higher is better)
- **Snippet** — A contextual excerpt of the matching code
- **Citations** — Links back to the original source for verification

## Citations

Every search result is traceable. Citations include:

- Repository name and URL
- Commit SHA
- File path and line numbers
- Permalink to the exact code location

This makes it easy to verify results and integrate findings into documentation or pull-request discussions.
