# Story 008: Host in UAT on Railway

**Priority:** High
**Component:** DevOps, Infrastructure
**Labels:** deployment, infrastructure

## Description

Deploy andy-code-index to a UAT (User Acceptance Testing) environment on Railway, following the same pattern used for other Andy ecosystem services (andy-auth, andy-docs). The deployment should include the API backend, the Angular frontend (served as static files or via the API), and a PostgreSQL database with pgvector.

## Acceptance Criteria

### Railway Configuration
- [ ] Railway project created for andy-code-index UAT
- [ ] PostgreSQL service provisioned with pgvector extension
- [ ] API service deployed from the `main` branch via GitHub integration
- [ ] Environment variables configured:
  - `ConnectionStrings__DefaultConnection` (Railway Postgres)
  - `AndyAuth__Authority` (pointing to andy-auth UAT)
  - `Rbac__ApiBaseUrl` (pointing to andy-rbac UAT)
  - `Embedding__ApiKey` (optional, can be set per user)
  - `ASPNETCORE_ENVIRONMENT=UAT`
- [ ] Health check endpoint configured for Railway health monitoring
- [ ] Custom domain: `andy-code-index-uat.up.railway.app` or similar

### Frontend Deployment
- [ ] Angular frontend built with `--configuration=production` (or `uat`)
- [ ] Frontend environment file for UAT points to the correct API URL and auth authority
- [ ] Static files served by the .NET API (or separate Railway service)

### CI/CD
- [ ] GitHub Actions workflow builds and deploys on push to `main`
- [ ] Build includes running unit tests before deployment
- [ ] Dockerfile or Railway nixpacks configuration for .NET 8 + Angular build

### OAuth Integration
- [ ] andy-auth UAT has `andy-code-index-web` client registered with UAT redirect URIs
- [ ] CORS configured to allow the UAT frontend origin
- [ ] JWT validation configured for the UAT andy-auth authority

### Testing & Documentation
- [ ] Smoke test: Can log in, add a repo, trigger sync, view enrichments, run search, chat
- [ ] `docs/DEPLOYMENT.md` created with Railway setup instructions
- [ ] `appsettings.UAT.json` created with UAT-specific configuration
- [ ] `README.md` updated with UAT URL; Apache 2.0 license confirmed

## Technical Notes

- Reference andy-auth's Railway deployment for patterns
- Railway provides persistent PostgreSQL -- ensure pgvector extension is enabled via init SQL
- Consider Railway's sleep policy for UAT (may need to keep alive or accept cold starts)
- Frontend can be embedded in the .NET API using `UseStaticFiles` + SPA fallback
- Git clone operations in UAT need access to the filesystem -- verify Railway's ephemeral storage is sufficient

## Test Plan

- Smoke: Full user journey on UAT (login, add repo, sync, search, chat)
- Auth: OAuth flow works end-to-end with andy-auth UAT
- RBAC: Permissions enforced via andy-rbac UAT
- Performance: Indexing a small repo completes within Railway's timeout limits
