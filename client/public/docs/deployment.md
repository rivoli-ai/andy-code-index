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

- **api** -- Node.js API server (port 3000)
- **client** -- Angular web application (port 4200)
- **db** -- PostgreSQL with pgvector extension
- **redis** -- Task queue and caching

### Custom Docker Compose

Override default settings with a `docker-compose.override.yml`:

```yaml
services:
  api:
    environment:
      - NODE_ENV=production
      - LOG_LEVEL=info
    ports:
      - "8080:3000"
```

## Environment Variables

### Required

| Variable | Description | Default |
|----------|-------------|---------|
| `DATABASE_URL` | PostgreSQL connection string | `postgresql://localhost:5432/codeindex` |
| `REDIS_URL` | Redis connection string | `redis://localhost:6379` |

### Optional

| Variable | Description | Default |
|----------|-------------|---------|
| `PORT` | API server port | `3000` |
| `EMBEDDING_API_KEY` | OpenAI-compatible embedding key | none |
| `EMBEDDING_BASE_URL` | Embedding provider URL | `https://api.openai.com/v1` |
| `LLM_API_KEY` | LLM provider key | none |
| `LLM_BASE_URL` | LLM provider URL | `https://api.openai.com/v1` |
| `AUTH_ENABLED` | Enable authentication | `false` |
| `AUTH_PROVIDER` | Auth provider (github, azure) | `github` |
| `MCP_ENABLED` | Enable MCP server | `true` |
| `LOG_LEVEL` | Logging level | `info` |

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
        proxy_pass http://localhost:4200;
    }

    location /api {
        proxy_pass http://localhost:3000;
    }
}
```

#### Caddy Example

```
codeindex.example.com {
    reverse_proxy /api/* localhost:3000
    reverse_proxy localhost:4200
}
```

Caddy automatically provisions and renews TLS certificates.

## Production Checklist

- [ ] Set `NODE_ENV=production`
- [ ] Configure HTTPS via reverse proxy
- [ ] Enable authentication
- [ ] Set strong database credentials
- [ ] Configure backup for PostgreSQL data
- [ ] Set appropriate resource limits on containers
- [ ] Monitor container health with Docker healthchecks
- [ ] Review and set log levels

## Scaling

For larger codebases, consider:

- Increasing PostgreSQL shared buffers and work memory.
- Running multiple API server instances behind a load balancer.
- Using a dedicated Redis instance with persistence enabled.
- Allocating more memory to embedding generation tasks.

## Updating

Pull the latest images and restart:

```bash
docker compose pull
docker compose up -d
```

Database migrations run automatically on startup.
