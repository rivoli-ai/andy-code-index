---
title: "Repositories"
order: 4
tags: [git, repositories]
---

# Repositories

A repository in Andy Code Index is any Git-based code repository you want to search. The system supports public and private repositories via HTTPS or SSH.

## Adding a Repository

```http
POST /api/v1/repositories
Content-Type: application/json

{
  "name": "my-service",
  "cloneUrl": "https://github.com/org/my-service.git",
  "branch": "main",
  "credentials": {
    "type": "ssh_key",
    "privateKey": "..."
  }
}
```

Once added, the repository is automatically queued for indexing.

## Supported Git Providers

- GitHub (cloud and Enterprise Server)
- GitLab (cloud and self-managed)
- Bitbucket (cloud and Data Center)
- Azure DevOps Repos
- Generic Git over HTTPS or SSH

## Authentication

| Method | Use Case |
|--------|----------|
| **Personal Access Token** | GitHub, GitLab, Bitbucket Cloud |
| **SSH Key** | Enterprise servers or restricted networks |
| **App/Bot Token** | Organization-wide access with fine-grained permissions |

Credentials are stored encrypted at rest and are only used during clone/fetch operations.

## Repository Settings

- **Default branch** — The branch used for indexing when no commit SHA is specified
- **Include / exclude patterns** — Glob patterns to filter files (e.g., `*.md`, `tests/**`)
- **Language overrides** — Force language detection for files with non-standard extensions
- **Auto-index** — Automatically re-index on a schedule or via webhook

## Webhooks

Configure a webhook in your Git provider to notify Andy Code Index on every push:

```
POST https://andy-code-index.example.com/api/v1/webhooks/git-push
```

The payload is validated with a shared secret, and the affected repository is queued for incremental indexing.

## Removing a Repository

Removing a repository deletes all associated indexed data, embeddings, and metadata. This action is irreversible.

```http
DELETE /api/v1/repositories/{repositoryId}
```
