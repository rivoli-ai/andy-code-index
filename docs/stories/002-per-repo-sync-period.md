# Story 002: Per-Repository Sync Period Configuration

**Priority:** Medium
**Component:** Backend API, Frontend, Database
**Labels:** feature, configuration

## Description

Add the ability to configure the sync period (polling interval) on a per-repository basis. Currently, all repositories share the same global sync schedule. Different repositories may have different activity levels and should support independent sync cadences (e.g., every 15 minutes for active repos, daily for archival repos, or manual-only).

## Acceptance Criteria

- [ ] `Repository` entity has a new `SyncIntervalMinutes` nullable int field (null = use global default)
- [ ] Database migration adds the column with a default of `null`
- [ ] `PUT /api/v1/repositories/{id}` accepts `syncIntervalMinutes` in the request body
- [ ] `GET /api/v1/repositories` and `GET /api/v1/repositories/{id}` return `syncIntervalMinutes` in the response
- [ ] Valid values: `0` (manual only), `15`, `30`, `60`, `120`, `360`, `720`, `1440` (1 day), or `null` (global default)
- [ ] The background sync scheduler respects per-repo intervals when deciding which repos to sync
- [ ] Frontend repository detail page shows a dropdown to select the sync interval
- [ ] Frontend repository list shows the next scheduled sync time or "Manual" indicator
- [ ] MCP tool `configure_repository` supports setting sync interval
- [ ] Unit tests cover:
  - [ ] Validation of allowed interval values
  - [ ] Sync scheduler correctly skips repos that haven't reached their interval
  - [ ] Default interval used when `syncIntervalMinutes` is null
- [ ] Integration tests cover the API endpoint with valid and invalid values
- [ ] `docs/design.md` updated with sync interval architecture
- [ ] `docs/requirements.md` updated with the new configuration requirement
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Add `SyncIntervalMinutes` to `Repository` entity and create an EF migration
- Update `TaskQueueService` or the sync scheduler to read per-repo intervals
- Consider adding a `LastSyncAttempt` timestamp to avoid drift
- Global default should remain configurable in `appsettings.json`

## Test Plan

- Unit: Repository with `syncIntervalMinutes = 60` not re-synced until 60 minutes elapsed
- Unit: Repository with `syncIntervalMinutes = 0` never auto-synced
- Unit: Repository with `syncIntervalMinutes = null` uses global default
- Integration: `PUT` endpoint accepts valid values, rejects invalid values (e.g., negative, non-standard)
- Frontend: Dropdown renders, saves, and reflects updated value after refresh
