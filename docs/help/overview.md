---
title: Andy Code Index Overview
slug: andy-code-index-overview
order: 1
tags: [code, search, embeddings]
---

# Andy Code Index Overview

Andy Code Index is the semantic code search service for the Andy ecosystem. It discovers repositories, walks file trees, computes embeddings, and serves hybrid (semantic + keyword) search with file-level citations.

## What it does

- Indexes every file in every registered repository — language-agnostic, parsed by tree-sitter where available.
- Stores embeddings in PostgreSQL via pgvector; runs hybrid search by fusing pgvector cosine similarity with PostgreSQL `tsvector` keyword hits.
- Returns citations (`path:line-range`) so the calling UI can deep-link the result into the editor.
- Re-indexes incrementally on `git pull`; full re-index is rare.
- Powers the chat panel's RAG when the conversation is grounded in repo content.

## Key concepts

- **Citation** — a `(repo, path, startLine, endLine)` tuple. Every search hit carries one; the UI uses it for previews and click-through.
- **Hybrid score** — Reciprocal Rank Fusion over the embedding rank and the keyword rank, the same pattern Conductor's help search uses.
- **Indexable file** — text files under a configurable size cap; binaries and generated files are skipped.

## Where it fits

The Conductor Code tab is a thin client over Code Index. The chat panel pulls citations from here when a prompt references repo state. Depends on bundled PostgreSQL (with pgvector) and on Auth for token validation.

## Configuration

Repository list and indexing thresholds live under `andy.code-index.*` in `andy-settings`. The pgvector connection string is baked into the embedded PostgreSQL bundle Conductor ships.

## Troubleshooting

- **Search returns nothing for a known file** — index is stale. Trigger a re-index from the Code tab or wait for the next poll cycle.
- **"pgvector extension not found"** — embedded PostgreSQL didn't load the extension. Check `~/Library/Logs/Conductor/services/postgres.log` for `CREATE EXTENSION vector` errors.
- **Indexing is slow** — large repos chew through embedding time. Add a `.codeindexignore` (gitignore-style) to skip vendored directories.
