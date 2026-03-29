# Story 006: Fix API Key Management and Settings UX

**Priority:** High
**Component:** Frontend (Angular), Backend API
**Labels:** bug, UX, feature

## Description

The Settings page has several UX issues and missing functionality:

1. **Enter key on save**: Pressing Enter after typing an API key does nothing. It should submit/save the key.
2. **LLM model selection**: Users cannot change the LLM model (e.g., switch between GPT-4o, GPT-4o-mini, Claude). The model dropdown is either missing or non-functional.
3. **Connection testing**: There is no way to test whether an API key works before saving or after saving.
4. **Change history user tracking**: The settings change log does not record which user made the change.

## Acceptance Criteria

### Enter Key Support
- [ ] Pressing Enter in any API key input field triggers the save action
- [ ] Visual feedback (brief success indicator) appears after saving

### LLM Model Selection
- [ ] Settings page displays a dropdown for LLM model selection
- [ ] Supported models: `gpt-4o`, `gpt-4o-mini`, `gpt-4-turbo`, `gpt-3.5-turbo` (and optionally custom model entry)
- [ ] Selected model is persisted in `UserSettings.LlmModel` (new field if needed)
- [ ] The enrichment pipeline uses the user's selected model when generating LLM enrichments
- [ ] `PUT /api/v1/settings` accepts `llmModel` in the request body

### Connection Testing
- [ ] A "Test Connection" button exists next to each API key input (Embedding, LLM)
- [ ] Clicking "Test Connection" calls a backend endpoint that makes a minimal API call to the provider
  - Embedding: Generate an embedding for "test" using the configured key and model
  - LLM: Send a minimal completion request ("Say hello") using the configured key and model
- [ ] Result displayed inline: green check + latency for success, red X + error message for failure
- [ ] New endpoint `POST /api/v1/settings/test-connection` with body `{ type: "embedding" | "llm" }`

### Change History
- [ ] `SettingsChangeLog` entity includes `UserId` and `UserEmail` fields
- [ ] Database migration adds the columns
- [ ] Settings controller extracts user identity from JWT claims and passes it to the service
- [ ] Change history UI displays the user who made each change
- [ ] `GET /api/v1/settings/history` response includes `userId` and `userEmail` fields

### General
- [ ] Unit tests cover Enter key handling, model persistence, connection test logic
- [ ] Integration tests cover the test-connection endpoint with valid and invalid keys
- [ ] Frontend tests cover the settings form interactions
- [ ] `docs/design.md` updated with settings architecture
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Check `SettingsComponent` for `(keydown.enter)` handling on input fields
- Add `LlmModel` column to `UserSettings` if not present
- Connection test should have a short timeout (5 seconds) to avoid hanging
- For the change log, extract user ID from `ClaimTypes.NameIdentifier` or `sub` claim
- Consider rate-limiting the test-connection endpoint to prevent API key abuse

## Test Plan

- Unit: Enter key in API key input triggers save action
- Unit: Model dropdown persists selection
- Unit: Connection test returns success/failure correctly
- Unit: Change log records user identity
- Integration: Save key, test connection, verify key works, check change log includes user
- Frontend: Keyboard navigation works (Tab between fields, Enter to save)
