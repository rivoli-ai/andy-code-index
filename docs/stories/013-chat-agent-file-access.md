# Story 013: Chat Agent Access to Specific Committed Files

**Priority:** High
**Component:** Backend API, Chat Service, MCP
**Labels:** feature, backend

## Description

Give the chat agent the ability to read specific files from indexed repositories by commit SHA, branch, or tag combined with a file path. This allows the agent to answer questions with precise, up-to-date file content rather than relying solely on pre-generated enrichments and embeddings.

The agent's system prompt should inform it that it has this capability, so it proactively fetches file content when a user asks about a specific file, function, or recent change.

## Acceptance Criteria

### File Access Capability
- [ ] New service method `GetFileContent(repositoryId, ref, filePath)` where `ref` can be:
  - A commit SHA (full or abbreviated)
  - A branch name (e.g., `main`, `develop`)
  - A tag name (e.g., `v1.0.0`)
- [ ] Returns file content as string with metadata (path, ref resolved to SHA, size, language)
- [ ] Handles errors gracefully: file not found, ref not found, binary file detection
- [ ] Works against the cloned repository on disk (uses `git show {ref}:{path}`)

### API Endpoint
- [ ] `GET /api/v1/repositories/{id}/files/{ref}/{**filePath}` returns file content
- [ ] Response includes: `content`, `resolvedSha`, `filePath`, `language`, `size`, `isBinary`
- [ ] Binary files return metadata only (no content), with `isBinary: true`
- [ ] Permission: `repository:read`

### Chat Agent Integration
- [ ] Chat system prompt updated to inform the agent it can fetch specific files:
  ```
  You have access to indexed repositories. When the user asks about a specific file,
  function, or code pattern, you can fetch the exact file content using:
  - A branch name (e.g., main) + file path
  - A commit SHA + file path
  - A tag + file path
  Use this to give precise answers based on actual source code, not just enrichment summaries.
  ```
- [ ] Chat service implements a tool/function-calling pattern where the agent can request file content mid-conversation
- [ ] Agent uses file content to provide precise, line-referenced answers
- [ ] Agent can fetch multiple files in a single turn if needed

### MCP Tool
- [ ] MCP tool `code_index_read_file` (or update existing `code_index_read_resource`) supports ref parameter
- [ ] Parameters: `repo_url`, `ref` (default: HEAD), `file_path`
- [ ] Shares implementation with the API endpoint

### Frontend
- [ ] Chat responses that reference specific files show clickable file paths
- [ ] File paths link to the file viewer (if available) or show inline content

### Testing & Documentation
- [ ] Unit tests for file access service (valid ref, invalid ref, binary detection, branch vs tag vs SHA)
- [ ] Integration tests for the API endpoint
- [ ] Unit tests for chat agent system prompt including file access instructions
- [ ] `docs/design.md` updated with chat agent architecture
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Use `git show {ref}:{path}` via `Process.Start` to read files from the cloned repo
- Resolve branch/tag to SHA first for caching consistency
- Consider caching file content by SHA+path (immutable once resolved)
- The existing `FilesController` may already have `blob/{gitRef}/{**filePath}` -- check and extend if so
- For the chat agent, consider using a simple tool-use pattern: the agent outputs a `[FETCH_FILE: repo, ref, path]` marker, the backend intercepts it, fetches the file, and injects the content back into the conversation
- Alternatively, if using OpenAI function calling, define a `get_file` function the model can invoke
- System prompt should be in a configurable location (not hardcoded in the chat service)
- Max file size limit (e.g., 100KB) to avoid overwhelming the context window

## Test Plan

- Unit: Fetch file by SHA, by branch name, by tag
- Unit: 404 for nonexistent file or ref
- Unit: Binary file detected and content omitted
- Unit: System prompt includes file access instructions
- Integration: Chat question "What does Program.cs do?" triggers file fetch and precise answer
- MCP: `code_index_read_file` returns correct content for given ref+path
