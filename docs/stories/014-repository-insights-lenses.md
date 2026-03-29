# Story 014: Repository Insights — Multi-Layer Analysis Lenses

**Priority:** High
**Component:** Backend API, Frontend, MCP, CLI
**Labels:** feature, epic

## Description

Generate structured, multi-layer insights for each indexed repository, providing a comprehensive understanding of the codebase from different angles. Each layer produces a distinct enrichment with a stable ID scheme that persists across git history. These insights power both in-app views and exportable reports.

## Insight Layers

### Layer 1: Features & Capabilities
- **What:** Discover and catalog the application's user-facing features/capabilities
- **Stable IDs:** Each feature gets a deterministic ID based on its name/path (e.g., `feat:auth:login`, `feat:search:semantic`). Same feature keeps same ID even as code evolves.
- **Output:** Hierarchical feature tree with: ID, name, description, entry points (files/functions), status (active/deprecated), complexity estimate
- **Enrichment subtype:** `FeatureMap`

### Layer 2: Technical Architecture
- **What:** High-level system structure — components, layers, communication patterns, data flow
- **Output:** Mermaid diagrams (component, sequence, deployment), textual description, identified patterns (MVC, CQRS, microservices, etc.)
- **Enrichment subtype:** `ArchitectureAnalysis`

### Layer 3: Technical Design
- **What:** How the application is designed — domain model, API surface, state management, error handling patterns
- **Output:** Mermaid class/ER diagrams, design pattern catalog, API contract summary
- **Enrichment subtype:** `DesignAnalysis`

### Layer 4: Implementation Analysis
- **What:** Important code patterns, code smells, potential improvements, cross-language analysis
- **Output:** Key code sections with annotations, improvement suggestions, complexity hotspots, language-specific idiom analysis
- **Enrichment subtype:** `ImplementationAnalysis`

### Layer 5: Dependencies
- **What:** Package dependencies, transitive deps, version freshness, known vulnerabilities, license compatibility
- **Output:** Dependency tree, outdated packages, security advisories, license matrix
- **Enrichment subtype:** `DependencyAnalysis` (extends existing `Dependencies`)

### Layer 6: Testing & Quality
- **What:** Test coverage, test pyramid analysis, test quality, missing test areas
- **Output:** Test pyramid shape (unit/integration/e2e ratios), coverage gaps, test quality score, improvement suggestions
- **Enrichment subtype:** `TestAnalysis`

### Layer 7: Security
- **What:** Security posture — auth patterns, secrets handling, input validation, OWASP top 10 exposure
- **Output:** Security checklist with findings, risk ratings, remediation suggestions
- **Enrichment subtype:** `SecurityAnalysis` (extends existing `Security`)

### Layer 8: Deployment & CI/CD
- **What:** Build pipelines, deployment configuration, environment management, release process
- **Output:** Pipeline analysis, deployment topology, environment matrix, improvement suggestions
- **Enrichment subtype:** `DeploymentAnalysis`

### Layer 9: Operations & Observability
- **What:** Logging patterns, monitoring, alerting, health checks, log level correctness, sensitive data in logs
- **Output:** Logging audit (missing/incorrect levels, PII exposure), monitoring gaps, operational readiness score
- **Enrichment subtype:** `OperationsAnalysis`

### Layer 10: Local Development
- **What:** How to set up and run the application locally — prerequisites, steps, common issues
- **Output:** Generated getting-started guide, verified setup steps, troubleshooting FAQ
- **Enrichment subtype:** `LocalSetupGuide`

## Stable Feature ID Scheme

Features must maintain consistent IDs across commits:

```
ID format: feat:{category}:{name}
Example:   feat:auth:oauth-login
           feat:search:semantic-search
           feat:chat:rag-conversation

Resolution rules:
1. ID derived from feature name (slugified), NOT from file path
2. If feature is renamed, map old ID → new ID in a migration
3. If feature is split, old ID deprecated, new IDs created
4. ID registry stored as enrichment with subtype FeatureRegistry
```

## Acceptance Criteria

### Backend
- [ ] New enrichment subtypes added to enum (FeatureMap, ArchitectureAnalysis, DesignAnalysis, ImplementationAnalysis, DependencyAnalysis, TestAnalysis, SecurityAnalysis, DeploymentAnalysis, OperationsAnalysis, LocalSetupGuide, FeatureRegistry)
- [ ] New LLM handlers for each layer (10 handlers), each with tailored prompts
- [ ] Handlers use existing enrichments as input context (not just raw files)
- [ ] Each handler sets CommitId for tracking changes over time
- [ ] Skip-if-unchanged via tree hash comparison
- [ ] New TaskOperations added to the pipeline (optional/configurable)
- [ ] API endpoint: `GET /api/v1/repositories/{id}/insights` — returns all insight layers
- [ ] API endpoint: `GET /api/v1/repositories/{id}/insights/{layer}` — returns specific layer
- [ ] Insights regenerated on each sync (if tree hash changed)

### Mermaid Diagram Generation
- [ ] LLM prompts instruct generation of Mermaid syntax for architecture, design, deployment
- [ ] Mermaid diagrams stored in enrichment content (markdown with ```mermaid blocks)
- [ ] Frontend renders Mermaid diagrams inline

### Frontend
- [ ] New "Insights" page/tab on repository detail
- [ ] Layer selector (tabs or accordion) for each insight type
- [ ] Mermaid diagram rendering (use mermaid.js library)
- [ ] Markdown rendering for textual insights
- [ ] Rating badges per layer (from analysis story #015)

### MCP Tools
- [ ] `code_index_insights` — params: repo_url, layer (optional) — returns all or specific layer
- [ ] `code_index_feature_map` — params: repo_url — returns feature hierarchy with stable IDs

### CLI
- [ ] `insights --repo <id>` — print all layers summary
- [ ] `insights --repo <id> --layer architecture` — print specific layer
- [ ] `insights --repo <id> --format mermaid` — output Mermaid diagrams

## Testing Plan

### Unit Tests
- Each handler produces correct enrichment subtype with CommitId
- Feature ID generation is deterministic (same input → same ID)
- Feature ID stability across commits (rename detection)
- Mermaid syntax validation (basic structure check)
- Skip-if-unchanged for each handler

### Integration Tests
- Full insight generation for a sample repo
- Insights accessible via API endpoint
- Feature map IDs stable after re-index
- Mermaid diagrams render without errors

## Documentation Plan
- `docs/design.md` — Insight layers architecture, feature ID scheme
- `docs/implementation.md` — Handler prompts, Mermaid generation
- `README.md` — Insights feature description
