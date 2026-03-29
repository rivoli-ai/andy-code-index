# Story 001: Graceful Backend Disconnection Handling

**Priority:** High
**Component:** Frontend (Angular), API
**Labels:** bug, UX, resilience

## Description

When the Angular frontend cannot connect to the andy-code-index API backend, the "Add Repository" button and other write actions should be disabled. Currently, the UI renders all controls regardless of backend availability, leading to silent failures when users attempt operations against an unreachable API.

The frontend should perform a health check on startup and periodically thereafter. When the backend is unreachable, the UI should clearly indicate the disconnected state and disable actions that require the backend.

## Acceptance Criteria

- [ ] Frontend calls `GET /health` on startup and every 30 seconds thereafter
- [ ] When the backend is unreachable:
  - [ ] A banner or indicator displays "Backend unavailable" at the top of the page
  - [ ] The "Add Repository" button is disabled with a tooltip explaining why
  - [ ] Sync, delete, and other write actions are disabled
  - [ ] Read-only views show cached data if available, or "Unable to load" otherwise
- [ ] When the backend becomes available again, the banner disappears and controls re-enable automatically
- [ ] Error states for individual API calls display user-friendly messages (not raw HTTP errors)
- [ ] Unit tests cover the health check service (mocked HTTP responses for success, timeout, error)
- [ ] Integration tests verify the disabled state of the "Add Repository" button when the backend is down
- [ ] `docs/design.md` updated with the health check architecture
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Create a `HealthService` in the Angular app that exposes a `BehaviorSubject<boolean>` for connection status
- Components inject `HealthService` and bind disabled states to `isConnected$`
- Use `catchError` and `timeout` operators in the health check observable
- Consider using the existing `/health` endpoint already defined in the API

## Test Plan

- Unit: `HealthService` returns `true` on 200, `false` on timeout/error
- Unit: `RepositoryListComponent` disables "Add" button when `isConnected$ === false`
- Integration: Start frontend without backend, verify UI shows disconnected state
- Integration: Start backend after frontend, verify UI recovers
