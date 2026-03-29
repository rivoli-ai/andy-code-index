# Story 009: Host in Production with rivoli.ai Public Site

**Priority:** High
**Component:** DevOps, Infrastructure
**Labels:** deployment, infrastructure, production

## Description

Deploy andy-code-index to the production environment under the rivoli.ai domain, making it publicly accessible alongside other Andy ecosystem services. This includes production-grade infrastructure, monitoring, backups, and integration with the production andy-auth and andy-rbac services.

## Acceptance Criteria

### Production Infrastructure
- [ ] Production deployment on Railway (or equivalent) with production-grade PostgreSQL + pgvector
- [ ] Custom domain configured: `code-index.rivoli.ai` (or similar subdomain)
- [ ] TLS certificate provisioned and auto-renewed
- [ ] Database backups configured (daily minimum)
- [ ] Environment variables set via Railway secrets (not committed to repo)

### Production Configuration
- [ ] `appsettings.Production.json` references environment variables for all secrets
- [ ] `AndyAuth__Authority` points to production andy-auth (`https://auth.rivoli.ai` or similar)
- [ ] `Rbac__ApiBaseUrl` points to production andy-rbac
- [ ] Rate limiting configured for public endpoints
- [ ] CORS restricted to production frontend origin(s)
- [ ] Logging configured with structured output (JSON) for log aggregation

### Frontend Production Build
- [ ] `environment.prod.ts` configured with production API URL and auth authority
- [ ] Angular build optimized with `--configuration=production` (tree-shaking, AOT, minification)
- [ ] OAuth client `andy-code-index-web` registered in production andy-auth with production redirect URIs

### Security
- [ ] No debug endpoints or developer exception pages exposed
- [ ] HTTPS enforced (HTTP redirects to HTTPS)
- [ ] Security headers configured (HSTS, X-Content-Type-Options, X-Frame-Options)
- [ ] API keys encrypted at rest in the database
- [ ] No test users or default credentials in production

### Monitoring
- [ ] Health check endpoint monitored externally
- [ ] Error tracking configured (Sentry, Application Insights, or equivalent)
- [ ] Key metrics tracked: request latency, error rate, indexing throughput

### CI/CD
- [ ] Production deployment triggered from tagged releases (not every push to main)
- [ ] Deployment requires manual approval or separate branch
- [ ] Rollback procedure documented

### Documentation
- [ ] `docs/DEPLOYMENT.md` updated with production setup
- [ ] `docs/security.md` updated with production security measures
- [ ] `README.md` updated with production URL; Apache 2.0 license confirmed
- [ ] Runbook for common operational tasks (restart, rollback, DB maintenance)

## Technical Notes

- Follow the same deployment pattern as andy-auth production
- Consider Railway's scaling options for production workloads
- Git clone operations need persistent or large ephemeral storage for production repos
- Consider separating the indexing worker from the API service for independent scaling
- Production should not auto-index on startup -- use explicit sync triggers

## Test Plan

- Smoke: Full user journey on production (login, add repo, sync, search, chat)
- Security: HTTPS enforced, no debug pages, security headers present
- Performance: Search responds within acceptable latency under load
- Backup: Verify database backup and restore procedure
- Rollback: Verify ability to roll back to previous version
