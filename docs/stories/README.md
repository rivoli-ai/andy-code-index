# Andy.CodeIndex - Stories Backlog

| # | Story | Priority | Labels |
|---|-------|----------|--------|
| [001](001-graceful-backend-disconnection.md) | Graceful Backend Disconnection Handling | High | bug, UX, resilience |
| [002](002-per-repo-sync-period.md) | Per-Repository Sync Period Configuration | Medium | feature, configuration |
| [003](003-commit-tracking-and-history.md) | Individual Commit Tracking and History Comparison | High | feature, core |
| [004](004-fix-chat-quick-questions-layout.md) | Fix Chat Quick Questions Layout and Verify Semantic Search | Medium | bug, UX |
| [005](005-mcp-feature-parity-and-testing.md) | MCP Feature Parity with API and Full Test Coverage | High | feature, testing, MCP |
| [006](006-fix-settings-key-management.md) | Fix API Key Management and Settings UX | High | bug, UX, feature |
| [007](007-organizations-and-teams.md) | Organization and Team Support | Medium | feature, multi-tenancy |
| [008](008-host-uat-railway.md) | Host in UAT on Railway | High | deployment, infrastructure |
| [009](009-host-prod-rivoli-ai.md) | Host in Production with rivoli.ai | High | deployment, production |
| [010](010-update-enrichments-descriptions.md) | Update Enrichments Type Descriptions | Low | documentation, UX |
| [011](011-chat-session-management.md) | Review and Improve Chat Session Management | Medium | feature, UX |
| [012](012-static-documentation-site.md) | Static Documentation Site with TOC Navigation | Medium | feature, documentation |

## Common Acceptance Criteria (all stories)

Every story must also satisfy:

- All new code has unit tests with passing results
- Integration tests cover API endpoints and key flows
- `./docs/` documentation is updated to reflect changes
- `README.md` is reviewed and accurate
- Apache 2.0 license is confirmed in `LICENSE` file
- No regressions in existing tests
