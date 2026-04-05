# MCP Server

CodeIndex includes a Model Context Protocol (MCP) server that exposes your indexed code to LLM tools like Claude. This lets external AI assistants search and understand your codebase.

## What Is MCP?

The Model Context Protocol is a standard for connecting AI models to external data sources. CodeIndex implements an MCP server that provides tools for code search, file reading, and repository browsing.

## Available Tools

### search_code

Search indexed code using semantic, keyword, or hybrid mode.

```json
{
  "name": "search_code",
  "parameters": {
    "query": "authentication middleware",
    "mode": "hybrid",
    "limit": 10
  }
}
```

### read_file

Read the full contents of an indexed file.

```json
{
  "name": "read_file",
  "parameters": {
    "repositoryId": "repo-id",
    "filePath": "src/auth/middleware.ts"
  }
}
```

### list_repositories

List all indexed repositories.

```json
{
  "name": "list_repositories",
  "parameters": {}
}
```

### list_files

Browse the file tree of a repository.

```json
{
  "name": "list_files",
  "parameters": {
    "repositoryId": "repo-id",
    "path": "src/"
  }
}
```

### get_enrichments

Retrieve LLM-generated documentation for a file.

```json
{
  "name": "get_enrichments",
  "parameters": {
    "repositoryId": "repo-id",
    "filePath": "src/auth/middleware.ts"
  }
}
```

## Integration with Claude

### Claude Desktop

Add CodeIndex as an MCP server in your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "code-index": {
      "command": "npx",
      "args": ["-y", "@anthropic/mcp-client", "http://localhost:3000/mcp"]
    }
  }
}
```

### Claude Code

Configure the MCP server in your Claude Code settings to enable code-aware conversations directly in your terminal.

## Server Configuration

The MCP server runs on the same port as the main API (default 3000). The endpoint is:

```
http://localhost:3000/mcp
```

### Environment Variables

- `MCP_ENABLED` -- Enable or disable the MCP server (default: true).
- `MCP_MAX_RESULTS` -- Maximum search results returned per query (default: 20).

## Security

The MCP server respects the same authentication settings as the REST API. When auth is enabled, MCP clients must provide valid credentials.

## Use Cases

- Ask Claude to review your codebase architecture.
- Search for patterns across repositories during a conversation.
- Generate documentation by having Claude read and summarize your code.
- Identify bugs by letting Claude analyze specific files with full context.
