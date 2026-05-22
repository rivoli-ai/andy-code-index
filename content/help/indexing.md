---
title: "Indexing"
order: 3
tags: [indexing, repositories]
---

# Indexing

Indexing is the process of turning your source code into a searchable knowledge base. Andy Code Index uses a multi-stage pipeline to extract, embed, and store code chunks.

## The Indexing Pipeline

1. **Clone / Pull** — The repository is cloned or updated to the latest commit.
2. **Parse** — Source files are parsed into an abstract syntax tree (AST) to identify functions, classes, and logical blocks.
3. **Chunk** — Code is split into meaningful chunks (typically functions or logical sections) with surrounding context.
4. **Embed** — Each chunk is converted into a high-dimensional vector embedding using a code-specific language model.
5. **Index** — Embeddings are stored in a vector database, and full-text tokens are indexed for BM25.
6. **Enrich** — Optional metadata (language, repository, commit SHA) is attached for filtering.

## Chunking Strategy

Chunks are designed to preserve context:

- Function-level granularity when possible
- Class-level for small classes
- Sliding windows for long functions with overlap
- Comments and docstrings are included to improve semantic quality

## Embedding Model

We use a fine-tuned code embedding model that understands:

- Multiple programming languages
- Natural language comments
- API signatures and type information

Embeddings are normalized to unit length so that cosine similarity equals dot product, enabling fast approximate nearest-neighbor search.

## Incremental Indexing

When a repository is re-indexed, only changed files are processed:

- Git diff determines modified, added, and deleted files
- Deleted chunks are removed from the vector and keyword indexes
- Modified chunks are re-embedded and updated in place
- Unchanged chunks are left untouched

This keeps indexing fast and resource-efficient for large codebases.

## Monitoring Index Status

Use the indexing API to check queue depth, current job status, and per-repository index freshness:

```http
GET /api/v1/indexing/status
GET /api/v1/indexing/status/{repositoryId}
```

## Troubleshooting

- **Large files** — Files above a size threshold are skipped or chunked more aggressively
- **Binary files** — Automatically detected and excluded
- **Parse errors** — Files with unrecoverable syntax errors fall back to line-based chunking
