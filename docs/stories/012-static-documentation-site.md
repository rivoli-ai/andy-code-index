# Story 012: Add Static Documentation Site with TOC Navigation

**Priority:** Medium
**Component:** Frontend (Angular)
**Labels:** feature, documentation

## Description

Add a static documentation section to the andy-code-index frontend, similar to what exists in andy-auth. The documentation should be authored in Markdown, rendered in the app, and include a table of contents (TOC) sidebar for navigation. This provides users with in-app reference documentation without needing to leave the application.

## Acceptance Criteria

### Documentation Pages
- [ ] `/docs` route in the Angular app renders the documentation viewer
- [ ] Left sidebar displays a TOC with all documentation sections
- [ ] Clicking a TOC entry navigates to that section (scroll or page)
- [ ] Content is authored in Markdown files and rendered via a Markdown library (e.g., `marked`, `ngx-markdown`)
- [ ] Code blocks are syntax-highlighted
- [ ] Documentation is searchable (full-text search across all pages)

### Documentation Content
The following documentation pages should be available:
- [ ] **Getting Started** -- Quick start guide, prerequisites, first-time setup
- [ ] **Architecture** -- System overview, component diagram, data flow
- [ ] **API Reference** -- REST API endpoints with request/response examples
- [ ] **MCP Tools** -- Complete list of MCP tools with descriptions and examples
- [ ] **Enrichments** -- What each enrichment type produces and how to use them
- [ ] **Search** -- How hybrid/semantic/keyword search works, query syntax
- [ ] **Chat** -- How RAG chat works, conversation management, tips
- [ ] **Settings** -- Configuration options, API keys, models
- [ ] **Security** -- Authentication, authorization, RBAC integration
- [ ] **Deployment** -- Local setup, Docker, Railway deployment
- [ ] **Contributing** -- Development setup, running tests, PR guidelines

### TOC Sidebar
- [ ] Sticky sidebar that scrolls independently from content
- [ ] Nested sections (e.g., API Reference > Repositories, API Reference > Search)
- [ ] Active section highlighted based on scroll position
- [ ] Collapsible sections for deep hierarchies
- [ ] Mobile: TOC collapses into a hamburger menu

### Integration
- [ ] Sidebar navigation includes a "Docs" link
- [ ] Documentation pages are accessible without authentication (public)
- [ ] Deep links work (e.g., `/docs/api-reference#search-endpoints`)

### Testing & Documentation
- [ ] Unit tests for the documentation viewer component
- [ ] Unit tests for TOC generation from Markdown headings
- [ ] All existing `docs/*.md` files migrated or referenced
- [ ] Content reviewed for accuracy against current codebase
- [ ] `README.md` updated to mention in-app documentation; Apache 2.0 license confirmed

## Technical Notes

- Reference andy-auth's documentation implementation for patterns:
  - `docs/ADMIN.md`, `docs/ARCHITECTURE.md`, `docs/LOCAL-SETUP.md`, etc.
  - Check how andy-auth renders these in its frontend
- Consider using `ngx-markdown` for Angular Markdown rendering with syntax highlighting
- TOC can be auto-generated from Markdown `## Heading` structure
- Store documentation as assets in `client/src/assets/docs/` or read from `docs/` at build time
- For search, consider building a simple inverted index at build time
- Anchor links should use slugified heading text (e.g., `## API Reference` -> `#api-reference`)

## Test Plan

- Unit: Documentation component renders Markdown content
- Unit: TOC generates correct structure from headings
- Unit: Navigation updates active section on scroll
- Integration: Deep link to a specific section works
- Content: Each documentation page loads without rendering errors
- Responsive: TOC collapses on mobile, content remains readable
