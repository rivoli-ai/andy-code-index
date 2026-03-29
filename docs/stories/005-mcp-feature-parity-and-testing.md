# Story 005: MCP Feature Parity with API and Full Test Coverage

**Priority:** High
**Component:** Backend API, MCP Server
**Labels:** feature, testing, MCP

## Description

Verify that all features available through the REST API are also available through MCP tools, that MCP tools share their implementation with the API controllers (no duplicated business logic), and that everything is fully tested. Any gaps in MCP coverage should be filled, and any duplicated logic should be refactored to share a common service layer.

## Acceptance Criteria

- [ ] Audit all REST API endpoints and map each to an MCP tool:
  - [ ] Repositories: list, create, get, delete, sync
  - [ ] Search: hybrid, semantic, keyword, filters
  - [ ] Enrichments: list, get by ID, counts
  - [ ] Settings: get, update, delete embedding key, re-embed
  - [ ] Chat: send message, get suggestions, get conversations, delete conversation
  - [ ] Queue/Tasks: list tasks, list pipelines, get task
  - [ ] Analytics: languages, top terms, file types, complex files, summary
  - [ ] Files: blob, ls, grep
  - [ ] Commits: list, get by SHA
  - [ ] Discovery: GitHub, Azure DevOps, sync
  - [ ] Indexing: history, sync status
- [ ] All MCP tools delegate to the same application services used by API controllers (no duplicated business logic in tool implementations)
- [ ] Missing MCP tools are implemented for any API endpoints not currently covered
- [ ] MCP tools return structured data consistent with API response DTOs
- [ ] Unit tests exist for every MCP tool (mocked services)
- [ ] Integration tests verify end-to-end MCP tool invocations via the MCP protocol
- [ ] MCP tool descriptions are accurate and include parameter documentation
- [ ] `docs/design.md` updated with MCP tool inventory and architecture
- [ ] `docs/security.md` updated with MCP authentication and authorization model
- [ ] `README.md` reviewed with accurate MCP tool count; Apache 2.0 license confirmed

## Technical Notes

- Current MCP tools are defined in `src/Andy.CodeIndex.Api/Mcp/CodeIndexTools.cs`
- Compare the list of `[McpServerTool]` methods against the controllers in `Controllers/`
- Ensure MCP tools use `[RequirePermission]` or equivalent authorization
- MCP tools should use the same DTOs as the API where possible
- Consider creating a shared `ICodeIndexFacade` or similar if needed to reduce duplication

## Test Plan

- Unit: Every MCP tool method has a corresponding test with mocked service
- Integration: Connect via MCP protocol, invoke each tool, verify response schema
- Comparison: For each API endpoint, invoke the equivalent MCP tool and verify responses match
- Auth: Verify MCP tools respect RBAC permissions (unauthorized user gets denied)
