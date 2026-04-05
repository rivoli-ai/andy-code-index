# API Reference

CodeIndex exposes a REST API for programmatic access to all features. The API server runs on port 3000 by default.

## Base URL

```
http://localhost:3000/api
```

All endpoints are prefixed with `/api`.

## Authentication

When authentication is enabled, include the access token in the Authorization header:

```
Authorization: Bearer <token>
```

In development mode, authentication is disabled by default.

## Repositories

### List Repositories

```
GET /api/repositories
```

Returns all indexed repositories with metadata.

### Add Repository

```
POST /api/repositories
Content-Type: application/json

{
  "url": "https://github.com/org/repo.git",
  "branch": "main"
}
```

### Get Repository

```
GET /api/repositories/:id
```

Returns details for a specific repository including file count and sync status.

### Delete Repository

```
DELETE /api/repositories/:id
```

Removes the repository and all associated data.

### Sync Repository

```
POST /api/repositories/:id/sync
```

Triggers an immediate synchronization.

## Search

### Search Code

```
POST /api/search
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
POST /api/enrichments/generate
Content-Type: application/json

{
  "repositoryId": "repo-id",
  "type": "file-summary"
}
```

### List Enrichments

```
GET /api/enrichments?repositoryId=repo-id
```

## Tasks

### List Tasks

```
GET /api/tasks
```

Returns all active and recent tasks with status and progress.

## Chat

### Send Message

```
POST /api/chat
Content-Type: application/json

{
  "message": "How does authentication work?",
  "conversationId": "optional-conversation-id"
}
```

### List Conversations

```
GET /api/chat/conversations
```

## Settings

### Get Settings

```
GET /api/settings
```

### Update Settings

```
PUT /api/settings
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
