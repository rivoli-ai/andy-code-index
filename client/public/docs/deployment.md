# Deployment

This guide covers deploying CodeIndex with Docker, configuring environment variables, and setting up HTTPS.

## Docker Deployment

### Quick Start

```bash
git clone https://github.com/rivoli-ai/andy-code-index.git
cd andy-code-index
cp .env.example .env
docker compose up -d
```

This starts all required services: the API server, the web client, and the database.

### Container Architecture

The application consists of these containers:

- **api** -- ASP.NET Core (.NET 8) API server, serving both REST and the Angular SPA (ports 7101 HTTPS, 7102 HTTP, 6201 docker client alias)
- **postgres** -- PostgreSQL 16 with pgvector extension (port 7436 on host)
- **ollama** -- Optional local embedding/LLM provider, enabled via the `ollama` profile

The background task queue is database-backed (no Redis dependency).

### Custom Docker Compose

Override default settings with a `docker-compose.override.yml`:

```yaml
services:
  api:
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Logging__LogLevel__Default=Information
    ports:
      - "8443:8443"
```

## Environment Variables

### Required

| Variable | Description | Default |
|----------|-------------|---------|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string | `Host=postgres;Port=5432;Database=andy_code_index;Username=andy_code_index;Password=...` |

### Optional

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Kestrel listen URLs | `https://+:8443;http://+:8080` |
| `Embedding__ApiKey` | OpenAI-compatible embedding key | none |
| `Embedding__BaseUrl` | Embedding provider URL | `https://api.openai.com/v1` |
| `Embedding__Model` | Embedding model | `text-embedding-3-small` |
| `Enrichment__ApiKey` | LLM provider key | none |
| `Enrichment__BaseUrl` | LLM provider URL | `https://api.openai.com/v1` |
| `Enrichment__Model` | LLM model for enrichments | `gpt-4o-mini` |
| `AndyAuth__Authority` | Andy.Auth OpenIddict authority URL (empty = anonymous dev mode) | empty |
| `AndyAuth__Audience` | Token audience | `urn:andy-code-index-api` |
| `Rbac__ApiBaseUrl` | Andy.Rbac base URL | none |
| `Rbac__ApplicationCode` | Application code in RBAC | `code-index` |
| `Indexing__DataDir` | Clone directory inside the container | `/data` |

## HTTPS Configuration

### Using a Reverse Proxy

The recommended approach is to place an Nginx or Caddy reverse proxy in front of CodeIndex.

#### Nginx Example

```nginx
server {
    listen 443 ssl;
    server_name codeindex.example.com;

    ssl_certificate /etc/ssl/certs/cert.pem;
    ssl_certificate_key /etc/ssl/private/key.pem;

    location / {
        proxy_pass https://localhost:7101;
    }
}
```

The API and SPA are served by the same ASP.NET Core process, so a single upstream is sufficient.

#### Caddy Example

```
codeindex.example.com {
    reverse_proxy https://localhost:7101
}
```

Caddy automatically provisions and renews TLS certificates.

## Production Checklist

- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Configure HTTPS via reverse proxy
- [ ] Configure Andy.Auth and Andy.Rbac for authentication and RBAC
- [ ] Set strong database credentials
- [ ] Configure backup for PostgreSQL data and the Data Protection keys volume
- [ ] Set appropriate resource limits on containers
- [ ] Monitor container health with Docker healthchecks (`/health`)
- [ ] Review and set log levels

## Scaling

For larger codebases, consider:

- Increasing PostgreSQL shared buffers and work memory.
- Running multiple API server instances behind a load balancer (sticky sessions not required; tasks are claimed from the database queue).
- Allocating more memory to embedding generation tasks and tuning `Indexing__WorkerCount`.

## Updating

Pull the latest images and restart:

```bash
docker compose pull
docker compose up -d
```

Database migrations run automatically on startup.
