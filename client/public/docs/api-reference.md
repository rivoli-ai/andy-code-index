# API Reference

CodeIndex exposes a REST API for programmatic access to all features. In Docker, the API runs on port `7101` (HTTPS) by default; the local .NET dev server runs on port `5101`.

## Base URL

```
https://localhost:7101/api/v1
```

All endpoints are prefixed with `/api/v1`. The OpenAPI spec is available at `/openapi.json` and Swagger UI at `/swagger` (Development).

## Authentication

When authentication is enabled, include the access token in the Authorization header:

```
Authorization: Bearer <token>
```

In development mode, authentication is disabled by default.

## Repositories

### List Repositories

```
GET /api/v1/repositories
```

Returns all indexed repositories with metadata.

### Add Repository

```
POST /api/v1/repositories
Content-Type: application/json

{
  "url": "https://github.com/org/repo.git",
  "branch": "main"
}
```

### Get Repository

```
GET /api/v1/repositories/:id
```

Returns details for a specific repository including file count and sync status.

### Delete Repository

```
DELETE /api/v1/repositories/:id
```

Removes the repository and all associated data.

### Sync Repository

```
POST /api/v1/repositories/:id/sync
```

Triggers an immediate synchronization.

## Search

### Search Code

```
POST /api/v1/search
Content-Type: application/json

{
  "query": "authentication middleware",
  "mode": "hybrid",
  "limit": 20,
  "repositoryId": "optional-repo-id"
}
```

Supported modes: `semantic`, `keyword`, `hybrid`.

## Enrichments

### Generate Enrichments

```
POST /api/v1/enrichments/generate
Content-Type: application/json

{
  "repositoryId": "repo-id",
  "type": "file-summary"
}
```

### List Enrichments

```
GET /api/v1/enrichments?repositoryId=repo-id
```

## Tasks

### List Tasks

```
GET /api/v1/queue
```

Returns all active and recent tasks with status and progress.

## Chat

### Send Message

```
POST /api/v1/chat
Content-Type: application/json

{
  "message": "How does authentication work?",
  "conversationId": "optional-conversation-id"
}
```

### List Conversations

```
GET /api/v1/chat/conversations
```

## Settings

### Get Settings

```
GET /api/v1/settings
```

### Update Settings

```
PUT /api/v1/settings
Content-Type: application/json

{
  "embeddingKey": "sk-...",
  "llmKey": "sk-..."
}
```

## Error Responses

All errors follow a consistent format:

```json
{
  "error": "Error message describing what went wrong",
  "statusCode": 400
}
```

Common status codes: 400 (bad request), 401 (unauthorized), 404 (not found), 500 (server error).
