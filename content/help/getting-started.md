---
title: "Getting Started"
order: 1
tags: [onboarding, quickstart]
---

# Getting Started with Andy Code Index

Andy Code Index is a semantic code search engine that helps you find, understand, and navigate code across your repositories. This guide will get you up and running in minutes.

## What is Semantic Code Search?

Unlike traditional text search, semantic search understands the **meaning** behind your query. You can ask natural-language questions like:

- "How do we handle authentication errors?"
- "Find the code that validates JWT tokens"
- "Where is the retry logic for HTTP requests?"

The system uses vector embeddings to match your intent with relevant code, even when the exact keywords don't appear in the source.

## Quick Start

1. **Add a repository** — Connect a Git repository via the API or web interface.
2. **Wait for indexing** — The system clones, parses, and embeds your code.
3. **Search** — Use the search endpoint with natural language or keywords.
4. **Explore results** — Each result includes the file path, line numbers, and a relevance score.

## Hybrid Scoring

Results are ranked using a hybrid approach that combines:

- **Semantic similarity** — Vector embedding cosine similarity
- **BM25 keyword relevance** — Classical full-text ranking
- **Reciprocal Rank Fusion (RRF)** — A proven method for merging heterogeneous rankings

This ensures you get the best of both worlds: conceptual matches and exact keyword hits.

## Next Steps

- Read about [Search](./search.md) to learn query syntax and filters
- Learn how [Indexing](./indexing.md) works under the hood
- Manage your [Repositories](./repositories.md)
