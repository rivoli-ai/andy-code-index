# Story 003: Individual Commit Tracking and History Comparison

**Priority:** High
**Component:** Backend API, Frontend, Database, MCP
**Labels:** feature, core

## Description

Implement full commit-level tracking so users can see which commit each repository is indexed at, browse previous commits, and compare enrichments between commits over time. Currently it is unclear which commit the index represents (presumably the latest on the default branch), and there is no way to navigate historical states.

The system should track the SHA of every indexed commit, associate enrichments with specific commits, and provide a UI to browse and compare enrichment changes across commits.

## Acceptance Criteria

- [ ] Each `IndexingRun` records the exact commit SHA it indexed
- [ ] `Repository` entity exposes `currentCommitSha` and `currentBranch` fields
- [ ] `GET /api/v1/repositories/{id}` returns `currentCommitSha`, `currentBranch`, and `lastIndexedAt`
- [ ] New endpoint `GET /api/v1/repositories/{id}/commits` returns a paginated list of indexed commits with:
  - Commit SHA, author, date, message
  - Count of enrichments generated for that commit
  - Whether it is the currently active index
- [ ] New endpoint `GET /api/v1/repositories/{id}/commits/{sha}/enrichments` returns enrichments for a specific commit
- [ ] New endpoint `GET /api/v1/repositories/{id}/commits/compare?from={sha}&to={sha}` returns:
  - Added enrichments (present in `to` but not `from`)
  - Removed enrichments (present in `from` but not `to`)
  - Changed enrichments (same file/type but different content)
- [ ] Frontend repository detail page shows:
  - Current commit SHA (abbreviated) with link to git provider
  - A commit history timeline with indexed commits
  - Ability to select two commits and view enrichment diff
- [ ] MCP tools include `get_indexed_commits` and `compare_commits`
- [ ] Enrichments are linked to commits via `CommitId` (already exists in schema -- verify it's populated)
- [ ] Unit tests cover:
  - [ ] Commit comparison logic (added, removed, changed)
  - [ ] Pagination of commit history
  - [ ] Correct SHA tracking during indexing
- [ ] Integration tests cover the new API endpoints
- [ ] Frontend tests cover the commit history view and comparison UI
- [ ] `docs/design.md` updated with commit tracking architecture and data model changes
- [ ] `docs/requirements.md` updated with commit history requirements
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- The `Enrichments` table already has a `CommitId` FK -- verify it's being set during indexing
- The `Commits` table exists -- verify it stores the SHA and is populated during `ScanCommit`
- Comparison should be based on enrichment `FilePath + Subtype` as the identity key
- Consider storing a content hash on enrichments to quickly detect changes without full content comparison
- For large repos, limit commit history to the last N indexed commits (configurable)

## Test Plan

- Unit: Comparison of two commit enrichment sets correctly identifies added/removed/changed
- Unit: Repository `currentCommitSha` updates after successful indexing
- Integration: Full flow -- index repo, re-index after new commit, compare enrichments
- Frontend: Commit timeline renders, commit selection triggers comparison view
- MCP: `get_indexed_commits` and `compare_commits` tools return correct data
