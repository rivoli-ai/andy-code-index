# Story 011: Review and Improve Chat Session Management

**Priority:** Medium
**Component:** Frontend (Angular), Backend API
**Labels:** feature, UX

## Description

Review and improve the chat session (conversation) management feature to bring it roughly in line with the experience available in ChatGPT. Users should be able to create, resume, rename, delete, and organize conversations with a clear, intuitive UI. The conversation list should be persistent, searchable, and display meaningful titles.

## Acceptance Criteria

### Conversation List (Left Panel)
- [ ] Conversations are grouped by time period: Today, Yesterday, Previous 7 Days, Previous 30 Days, Older
- [ ] Each conversation shows: auto-generated title, last message preview, timestamp
- [ ] Conversations are sorted by most recently updated
- [ ] Search/filter conversations by title or content
- [ ] Conversation count displayed

### Conversation Actions
- [ ] Rename: Click on conversation title to edit inline, or via a context menu
- [ ] Delete: Confirmation prompt before deleting
- [ ] New Chat: Clear button at the top that starts a fresh conversation
- [ ] Pin: Ability to pin important conversations to the top of the list

### Auto-Title Generation
- [ ] First message or first few words used as default title
- [ ] Option to let the backend generate a smarter title via LLM (summarize the conversation topic)
- [ ] Titles are editable after generation

### Conversation Persistence
- [ ] Conversations survive page refresh and browser restart
- [ ] All messages in a conversation are loaded when resuming
- [ ] Conversation state includes: selected repository filter, message history, sources

### Backend API
- [ ] `GET /api/v1/chat/conversations` supports `?search=`, `?limit=`, `?offset=` parameters
- [ ] `PATCH /api/v1/chat/conversations/{id}` supports updating title and pinned status
- [ ] `GET /api/v1/chat/conversations/{id}` returns full conversation with all messages and sources
- [ ] Conversations store the repository context they were created with

### Testing & Documentation
- [ ] Unit tests for conversation service (create, list, rename, delete, pin, search)
- [ ] Frontend tests for conversation list rendering, grouping, and actions
- [ ] Integration tests for conversation API endpoints
- [ ] `docs/design.md` updated with chat architecture
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Current implementation stores conversations in `ChatConversations` and `ChatMessages` tables
- Auto-title can use the first user message truncated to ~50 chars as a simple approach
- LLM-based title generation should be optional and async (don't block the first response)
- Consider a `pinnedAt` timestamp column for pinning (null = unpinned, non-null = pinned, sorted by pinnedAt)
- Search should query both conversation titles and message content

## Test Plan

- Unit: Conversations grouped correctly by date
- Unit: Search filters conversations by title and content
- Unit: Rename updates title, pin moves conversation to top
- Integration: Full conversation lifecycle (create, send messages, rename, pin, delete)
- Frontend: Conversation list updates in real time after actions
- Persistence: Refresh page, verify conversation list and last conversation are preserved
