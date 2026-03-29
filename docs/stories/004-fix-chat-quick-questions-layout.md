# Story 004: Fix Chat Quick Questions Layout and Verify Semantic Search

**Priority:** Medium
**Component:** Frontend (Angular)
**Labels:** bug, UX

## Description

The chat page's left panel has layout issues: quick question categories and subcategories are not centered within their panel, and the right margin is too small causing visual imbalance. Additionally, verify that the quick questions include semantic search questions and that the questions are relevant to indexed code.

## Acceptance Criteria

- [ ] Category tiles in the left panel are visually centered with equal left and right padding
- [ ] Question items have consistent padding on both sides of the panel
- [ ] The gap between the left panel and the chat area is visually balanced
- [ ] Category grid items wrap correctly on narrow sidebar widths
- [ ] Quick questions include semantic search questions (e.g., "Find functions similar to...", "Search for code that handles...")
- [ ] Questions are organized into clear categories that match the enrichment types
- [ ] All font sizes in the sidebar use the app's CSS variables (`--font-xs`, `--font-sm`, `--font-base`) -- no hardcoded pixel/rem values
- [ ] Responsive behavior: on mobile (<768px), the sidebar collapses gracefully
- [ ] Unit tests verify the `ChatComponent` renders categories and questions
- [ ] Visual inspection on at least 2 viewport sizes (1440px, 768px)
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Audit all `padding`, `margin`, and `gap` values in `chat.component.ts` styles
- Replace hardcoded font sizes (e.g., `0.75rem`, `0.65rem`, `0.7rem`) with CSS variable references
- Ensure `.sidebar-section`, `.question-list`, and `.category-grid` all use the same horizontal padding
- Review the `SuggestionDimension` categories returned by the backend `GET /api/v1/chat/suggestions` endpoint to ensure semantic search questions are included

## Test Plan

- Unit: ChatComponent renders expected number of categories and questions
- Unit: Category selection filters questions correctly
- Visual: Screenshot comparison at 1440px and 768px widths
- Manual: Verify semantic search questions appear (e.g., "What patterns does the codebase use for error handling?")
