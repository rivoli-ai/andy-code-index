# Andy.CodeIndex -- Security

This document covers authentication, authorization, RBAC permissions, API key management, and MCP security for Andy.CodeIndex.

## 1. Authentication

### 1.1 Overview

Andy.CodeIndex uses OAuth 2.0 with PKCE via Andy.Auth (an OpenIddict-based authorization server). All API endpoints require a valid JWT Bearer token except `/health` and OAuth metadata endpoints.

### 1.2 Backend Configuration

The backend authenticates requests using the `Andy.Auth` NuGet package:

```csharp
builder.Services.AddAndyAuth(builder.Configuration);
```

Configuration in `appsettings.json`:

```json
{
  "AndyAuth": {
    "Provider": "AndyAuth",
    "Authority": "https://localhost:5001",
    "Audience": "urn:andy-code-index-api",
    "RequireHttpsMetadata": false
  }
}
```

- **Authority**: URL of the Andy.Auth OpenIddict server
- **Audience**: The resource identifier for token validation
- **RequireHttpsMetadata**: Set to `false` for local development with self-signed certificates

When `Authority` is empty, the backend falls back to a permissive dev mode where all requests are allowed without authentication. This is only for initial setup; production and local development with Andy.Auth both enforce JWT validation.

### 1.3 JWT Validation

The backend validates:

- Token signature (verified against Andy.Auth's signing keys via OIDC discovery)
- Issuer (must match the configured authority)
- Audience (must match `urn:andy-code-index-api` or the MCP resource URL)
- Expiry (tokens must not be expired, with a 5-minute clock skew tolerance)

Claims extracted from the JWT:

| Claim | Usage |
|-------|-------|
| `sub` | User identity for RBAC permission checks and per-user settings |
| `email` | Display in UI, settings audit trail |
| `name` | Display in sidebar |

### 1.4 Frontend Authentication Flow

The Angular frontend implements OAuth 2.0 Authorization Code flow with PKCE:

```
1. User clicks "Sign In"
2. Frontend generates PKCE code_verifier (64 bytes) and code_challenge (SHA-256)
3. Frontend generates state parameter (CSRF protection)
4. Redirect to Andy.Auth /connect/authorize with:
   - client_id: andy-code-index-web
   - redirect_uri: https://localhost:4201/callback
   - response_type: code
   - scope: openid profile email urn:andy-code-index-api offline_access
   - code_challenge + code_challenge_method: S256
   - state
5. User authenticates at Andy.Auth
6. Andy.Auth redirects to /callback?code=...&state=...
7. Frontend validates state, exchanges code + code_verifier for tokens
8. Stores access_token, id_token, refresh_token in localStorage
9. Redirects to the originally requested page
```

### 1.5 Token Management

| Token | Storage | Purpose |
|-------|---------|---------|
| access_token | localStorage | API authorization (Bearer header) |
| id_token | localStorage | User identity claims (name, email) |
| refresh_token | localStorage | Silent token renewal |
| token_expiry | localStorage | Expiry timestamp for proactive refresh |

**Token refresh**: The `AuthService` automatically refreshes expired tokens using the refresh token before making API calls. A promise-based lock prevents race conditions when multiple concurrent requests trigger refresh simultaneously.

**Auth interceptor**: All HTTP requests to `/api/*` endpoints have the Bearer token injected automatically. On 401 responses, the user is signed out and redirected to the login page.

**Auth guard**: Protected routes check `AuthService.isAuthenticated()` before allowing access. Unauthenticated users are redirected to `/login` with the attempted URL saved for post-login redirect.

### 1.6 Client Registration in Andy.Auth

The `andy-code-index-web` client is registered in Andy.Auth's `DbSeeder.cs`:

```
Client ID:      andy-code-index-web
Client Type:    Public (no secret -- SPA)
Consent Type:   Implicit (no consent screen)
Grant Types:    authorization_code, refresh_token
Scopes:         openid, profile, email, urn:andy-code-index-api, offline_access
Redirect URIs:  https://localhost:4201/callback
Post-Logout:    https://localhost:4201/
```

The `urn:andy-code-index-api` scope is registered as an OpenIddict scope resource, so tokens issued with this scope include it as an audience claim.

## 2. Authorization (RBAC)

### 2.1 Overview

Andy.CodeIndex uses Andy.Rbac for role-based access control. The `Andy.Rbac.Client` NuGet package provides:

- Declarative `[RequirePermission]` attributes on controller actions
- HTTP-based permission resolution against the Andy.Rbac API
- In-memory caching to minimize network overhead

### 2.2 Configuration

```json
{
  "Rbac": {
    "ApiBaseUrl": "https://localhost:5003",
    "ApplicationCode": "code-index"
  }
}
```

Registration in `Program.cs`:

```csharp
builder.Services.AddRbacClient(options =>
{
    options.ApiBaseUrl = rbacBaseUrl;
    options.ApplicationCode = "code-index";
});
```

When `Rbac:ApiBaseUrl` is empty, RBAC checks are not enforced (dev fallback).

### 2.3 Permission Model

Application code: `code-index`

| Resource Type | Actions | Supports Instances | Description |
|---------------|---------|-------------------|-------------|
| repository | read, write, delete, execute | Yes | Code repositories. `execute` covers sync and reindex operations. |
| search | read | No | Semantic, keyword, and hybrid search. |
| enrichment | read | No | Architecture docs, API docs, wiki, cookbook, commit history, dependencies. |
| task | read | No | Indexing task queue and pipeline progress. |
| settings | read, write | No | Per-user API key management and configuration. |

Permission format: `code-index:resource:action` (short form `resource:action` when application code is configured).

### 2.4 Roles

| Role | Permissions (9 total) | Use Case |
|------|----------------------|----------|
| admin | All 9 permissions | Full access: manage repos, search, view enrichments/tasks, configure settings |
| user | 5 read permissions (repository, search, enrichment, task, settings) | Read-only access to all resources |

### 2.5 Controller Permission Mapping

Every API action has a `[RequirePermission]` attribute:

| Controller | Action | Permission |
|------------|--------|------------|
| RepositoriesController | List, GetById | `repository:read` |
| RepositoriesController | Create | `repository:write` |
| RepositoriesController | Delete | `repository:delete` |
| RepositoriesController | Sync | `repository:execute` |
| AnalyticsController | All endpoints | `repository:read` |
| CommitsController | List, GetById | `repository:read` |
| FilesController | Blob, Ls, Grep | `repository:read` |
| DiscoveryController | GitHub, AzureDevOps | `repository:read` |
| DiscoveryController | Sync (import) | `repository:write` |
| SearchController | Hybrid, Semantic, Keyword, Filters | `search:read` |
| ChatController | Chat | `search:read` |
| EnrichmentsController | Query, Counts, GetById | `enrichment:read` |
| QueueController | List, Pipelines, GetById | `task:read` |
| IndexingController | History | `repository:read` |
| IndexingController | Sync status | `task:read` |
| SettingsController | Get, History | `settings:read` |
| SettingsController | Update, Delete key, Re-embed | `settings:write` |

### 2.6 Caching and Performance

The Andy.Rbac.Client uses an in-memory cache to avoid round trips to the RBAC server on every request:

- **Cache TTL**: 5 minutes (configurable via `Cache.Expiration`)
- **First request**: Fetches all permissions for the user from the RBAC server via HTTP
- **Subsequent requests**: Resolved from in-memory cache (no network call)
- **Auto-invalidation**: Cache is cleared when roles are assigned or revoked via `IRbacClient`
- **Manual invalidation**: If permissions are changed directly in the RBAC database or admin UI, changes propagate within the cache TTL (up to 5 minutes)

For stricter revocation requirements, the TTL can be reduced or a distributed cache (Redis) can be configured:

```csharp
builder.Services.AddRbacClient(options =>
{
    options.Cache.Enabled = true;
    options.Cache.Expiration = TimeSpan.FromMinutes(1); // Tighter TTL
    options.Cache.UseDistributedCache = true;
    options.Cache.RedisConnectionString = "localhost:6379";
});
```

### 2.7 Authorization Flow

```
HTTP Request with JWT
    |
    v
[JWT Validation] -- 401 --> Unauthorized (no/invalid token)
    | valid
    v
[Extract SubjectId from "sub" claim]
    |
    v
[RequirePermission("resource:action")]
    |
    v
[IRbacClient.HasPermissionAsync(subjectId, "code-index:resource:action")]
    |
    +-- Check in-memory cache first
    |   +-- Hit: return cached result
    |   +-- Miss: HTTP call to RBAC server, cache result
    |
    +-- allowed --> Process request
    +-- denied  --> 403 Forbidden
```

## 3. API Key Management

### 3.1 Per-User API Keys

Users can configure their own API keys for embedding and LLM services via the Settings page. Keys are encrypted at rest using ASP.NET Core Data Protection.

### 3.2 Key Resolution Chain

For embedding operations:

```
1. User embedding key (UserSettings.EmbeddingApiKey, encrypted) --> source: "user"
2. System embedding key (Embedding:ApiKey from appsettings)     --> source: "system"
3. No key available                                             --> source: "none"
```

For LLM/chat operations (4-tier):

```
1. User LLM key (UserSettings.LlmApiKey)         --> source: "user-llm"
2. User embedding key (fallback)                  --> source: "user-embedding"
3. System LLM key (Enrichment:ApiKey)             --> source: "system-llm"
4. System embedding key (Embedding:ApiKey)        --> source: "system-embedding"
```

### 3.3 Key Storage

- Keys are encrypted using ASP.NET Core Data Protection (`IDataProtector`)
- Stored in the `UserSettings` table with the user's subject ID
- Displayed masked in the UI: `***...XXXX` (last 4 characters)
- All key changes are logged in `SettingsChangeLog` with timestamp, action, and masked values

## 4. MCP Security

### 4.1 MCP Endpoint

The MCP server is mounted at `/mcp` using HTTP Streamable transport:

```csharp
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
app.MapMcp("/mcp").RequireCors("AllowMcpClients").RequireAuthorization();
```

### 4.2 OAuth Protected Resource Metadata (RFC 8707)

When Andy.Auth is configured, the MCP endpoint publishes OAuth metadata:

```
GET /.well-known/oauth-protected-resource
```

Returns:

```json
{
  "resource": "https://localhost:5101/mcp",
  "authorization_servers": ["https://localhost:5001"],
  "scopes_supported": ["openid", "profile", "email"]
}
```

MCP clients (Claude Desktop, Cursor, Claude Code) use this metadata to discover the authorization server and initiate the OAuth flow.

### 4.3 MCP Authentication Flow

```
MCP Client (Claude Desktop / Cursor / Claude Code)
    |
    v
GET /.well-known/oauth-protected-resource
    |  --> discovers Andy.Auth as authorization server
    v
OAuth 2.0 flow with Andy.Auth (resource parameter = MCP server URL)
    |  --> receives JWT with MCP resource URL as audience
    v
MCP requests to /mcp with Authorization: Bearer <token>
    |
    v
Backend validates JWT (audience includes MCP resource URL)
    |
    v
RBAC permission checks apply (same as REST API)
    |
    v
MCP tool executes with user context
```

### 4.4 MCP Client Configuration

For local development without auth (dev mode):

```json
{
  "mcpServers": {
    "andy-code-index": {
      "type": "streamable-http",
      "url": "http://localhost:5100/mcp"
    }
  }
}
```

With auth enabled, MCP clients handle the OAuth flow automatically using the protected resource metadata.

### 4.5 CORS for MCP

The `AllowMcpClients` CORS policy permits any origin for MCP clients, since tools like Claude Desktop and Cursor connect from various origins:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMcpClients", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
```

## 5. Development Setup

### 5.1 Prerequisites

- Andy.Auth running on `https://localhost:5001`
- Andy.Rbac running on `https://localhost:5003`
- PostgreSQL databases for both services

### 5.2 Andy.Auth Setup

1. Start the Andy.Auth database: `docker compose up postgres -d` (in andy-auth directory)
2. Start Andy.Auth: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Andy.Auth.Server --urls "https://localhost:5001"`
3. The `DbSeeder` automatically registers:
   - Client: `andy-code-index-web` (public, PKCE, redirect to `https://localhost:4201/callback`)
   - Scope: `urn:andy-code-index-api`
4. Create a user account at `https://localhost:5001/register` if you don't have one

The client is registered in `andy-auth/src/Andy.Auth.Server/Data/DbSeeder.cs`:

```csharp
await manager.CreateAsync(new OpenIddictApplicationDescriptor
{
    ClientId = "andy-code-index-web",
    DisplayName = "Andy Code Index Web",
    ClientType = OpenIddictConstants.ClientTypes.Public,
    ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
    Permissions =
    {
        OpenIddictConstants.Permissions.Endpoints.Authorization,
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.Scopes.Email,
        OpenIddictConstants.Permissions.Scopes.Profile,
        OpenIddictConstants.Permissions.Scopes.Roles,
        OpenIddictConstants.Permissions.Prefixes.Scope + "offline_access",
        "scp:urn:andy-code-index-api",
        OpenIddictConstants.Permissions.ResponseTypes.Code
    },
    RedirectUris = { new Uri("https://localhost:4201/callback") },
    PostLogoutRedirectUris = { new Uri("https://localhost:4201/") }
});
```

The scope is registered alongside:

```csharp
await manager.CreateAsync(new OpenIddictScopeDescriptor
{
    Name = "urn:andy-code-index-api",
    DisplayName = "Andy Code Index API",
    Resources = { "urn:andy-code-index-api" }
});
```

To add a production redirect URI, update the `RedirectUris` and `PostLogoutRedirectUris` lists with the deployed URL.

### 5.3 RBAC Setup

The following RBAC data must be present in the Andy.Rbac database. This can be seeded via SQL against the `andy_rbac` database (running in the `andy-rbac-db` Docker container):

**Step 1: Register the application**

```sql
INSERT INTO applications ("Id", "Code", "Name", "Description", "CreatedAt")
VALUES (
  'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
  'code-index',
  'Andy Code Index',
  'Semantic code indexing service',
  NOW()
);
```

**Step 2: Create resource types**

```sql
INSERT INTO resource_types ("Id", "ApplicationId", "Code", "Name", "Description", "SupportsInstances") VALUES
('11111111-1111-1111-1111-111111111001', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'repository', 'Repository', 'Code repositories', true),
('11111111-1111-1111-1111-111111111002', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'search', 'Search', 'Code search', false),
('11111111-1111-1111-1111-111111111003', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'enrichment', 'Enrichment', 'Enrichment data', false),
('11111111-1111-1111-1111-111111111004', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'task', 'Task', 'Indexing tasks', false),
('11111111-1111-1111-1111-111111111005', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'settings', 'Settings', 'User settings', false);
```

**Step 3: Create permissions** (cross join resource types with the appropriate actions)

```sql
INSERT INTO permissions ("Id", "ResourceTypeId", "ActionId", "Description")
SELECT gen_random_uuid(), rt."Id", a."Id", rt."Name" || ' ' || a."Name"
FROM resource_types rt
CROSS JOIN actions a
JOIN applications app ON rt."ApplicationId" = app."Id"
WHERE app."Code" = 'code-index'
AND (
  (rt."Code" = 'repository' AND a."Code" IN ('read', 'write', 'delete', 'execute'))
  OR (rt."Code" = 'search' AND a."Code" = 'read')
  OR (rt."Code" = 'enrichment' AND a."Code" = 'read')
  OR (rt."Code" = 'task' AND a."Code" = 'read')
  OR (rt."Code" = 'settings' AND a."Code" IN ('read', 'write'))
);
```

**Step 4: Create roles**

```sql
INSERT INTO roles ("Id", "ApplicationId", "Code", "Name", "Description", "IsSystem", "CreatedAt") VALUES
('22222222-2222-2222-2222-222222222001', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'admin', 'Administrator', 'Full access to Code Index', false, NOW()),
('22222222-2222-2222-2222-222222222002', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'user', 'User', 'Standard user access', false, NOW());
```

**Step 5: Assign permissions to roles**

```sql
-- Admin: all 9 permissions
INSERT INTO role_permissions ("RoleId", "PermissionId")
SELECT '22222222-2222-2222-2222-222222222001', p."Id"
FROM permissions p
JOIN resource_types rt ON p."ResourceTypeId" = rt."Id"
JOIN applications app ON rt."ApplicationId" = app."Id"
WHERE app."Code" = 'code-index';

-- User: read-only (5 permissions)
INSERT INTO role_permissions ("RoleId", "PermissionId")
SELECT '22222222-2222-2222-2222-222222222002', p."Id"
FROM permissions p
JOIN resource_types rt ON p."ResourceTypeId" = rt."Id"
JOIN actions a ON p."ActionId" = a."Id"
JOIN applications app ON rt."ApplicationId" = app."Id"
WHERE app."Code" = 'code-index' AND a."Code" = 'read';
```

**Step 6: Create a subject and assign the admin role**

Find your user's `Id` from Andy.Auth:

```sql
-- In andy-auth database
SELECT "Id", "UserName", "Email" FROM "AspNetUsers";
```

Then in the RBAC database:

```sql
-- Create subject (use the Andy.Auth user Id as ExternalId)
INSERT INTO subjects ("Id", "ExternalId", "Provider", "Type", "Email", "DisplayName", "IsActive", "CreatedAt")
VALUES (gen_random_uuid(), '<andy-auth-user-id>', 'andy-auth', 0, '<email>', '<display-name>', true, NOW());

-- Assign admin role
INSERT INTO subject_roles ("Id", "SubjectId", "RoleId", "GrantedAt")
SELECT gen_random_uuid(), s."Id", '22222222-2222-2222-2222-222222222001', NOW()
FROM subjects s
WHERE s."ExternalId" = '<andy-auth-user-id>';
```

All SQL commands should be run via:

```bash
docker exec andy-rbac-db psql -U postgres -d andy_rbac -c "<SQL>"
```

### 5.4 Running Without Auth

To run without authentication (initial development):

1. Remove or leave empty the `AndyAuth:Authority` in `appsettings.Development.json`
2. Remove or leave empty the `Rbac:ApiBaseUrl`
3. The backend falls back to permissive mode (all requests allowed, user ID defaults to "anonymous")

## 6. Security Checklist

| Area | Status | Notes |
|------|--------|-------|
| JWT Bearer authentication | Enforced | Via Andy.Auth with OIDC discovery |
| PKCE flow (frontend) | Implemented | SHA-256 code challenge, state parameter |
| Token refresh | Implemented | Automatic refresh with race condition guard |
| RBAC permission checks | Enforced | 35 `[RequirePermission]` attributes across all controllers |
| Permission caching | Enabled | 5-minute in-memory cache via Andy.Rbac.Client |
| API key encryption | Enabled | ASP.NET Core Data Protection |
| Settings audit trail | Enabled | All key changes logged with timestamps |
| MCP OAuth metadata | Published | RFC 8707 at `/.well-known/oauth-protected-resource` |
| CORS | Configured | Angular app restricted, MCP clients permissive |
| HTTPS | Required | Self-signed dev cert, production requires real cert |
