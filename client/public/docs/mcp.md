# MCP Server

CodeIndex includes a Model Context Protocol (MCP) server that exposes your indexed code to LLM tools like Claude. This lets external AI assistants search and understand your codebase.

## What Is MCP?

The Model Context Protocol is a standard for connecting AI models to external data sources. CodeIndex implements an MCP server that provides tools for code search, file reading, and repository browsing.

## Available Tools

CodeIndex exposes 58 MCP tools, all prefixed with `code_index_`. They are grouped into Query/Search, Enrichments/Insights, Management, and Discovery. A representative subset:

### code_index_hybrid_search

Search indexed code using Reciprocal Rank Fusion over semantic + BM25 results.

```json
{
  "name": "code_index_hybrid_search",
  "parameters": {
    "query": "authentication middleware",
    "limit": 10
  }
}
```

`code_index_semantic_search` and `code_index_keyword_search` are also available for single-mode search.

### code_index_fetch_file

Read the full contents of an indexed file at a given ref.

```json
{
  "name": "code_index_fetch_file",
  "parameters": {
    "repo_url": "https://github.com/org/repo",
    "ref": "main",
    "path": "src/auth/middleware.ts"
  }
}
```

### code_index_repositories

List all indexed repositories.

```json
{
  "name": "code_index_repositories",
  "parameters": {}
}
```

### code_index_ls

Browse the file tree of a repository.

```json
{
  "name": "code_index_ls",
  "parameters": {
    "repo_url": "https://github.com/org/repo",
    "pattern": "src/**"
  }
}
```

### code_index_query_enrichments

Retrieve LLM-generated documentation (architecture, API docs, wiki, cookbook, etc.).

```json
{
  "name": "code_index_query_enrichments",
  "parameters": {
    "repo_url": "https://github.com/org/repo",
    "subtype": "APIDocs"
  }
}
```

The full tool list is documented in the project README (`MCP Tools` section) and discoverable via the MCP `tools/list` request.

## Integration with Claude

### Claude Desktop

Add CodeIndex as an MCP server in your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "code-index": {
      "url": "https://localhost:7101/mcp"
    }
  }
}
```

CodeIndex implements the streamable-HTTP MCP transport, so any client that speaks HTTP MCP (including Claude Desktop and Claude Code) can connect directly to the `/mcp` endpoint.

### Claude Code

Configure the MCP server in your Claude Code settings to enable code-aware conversations directly in your terminal.

## Server Configuration

The MCP server runs on the same port as the main API. The default endpoints are:

```
https://localhost:7101/mcp   (Docker)
https://localhost:5101/mcp   (local .NET dev server)
```

OAuth Protected Resource Metadata for MCP clients is exposed at `/.well-known/oauth-protected-resource` (RFC 8707).

## Security

The MCP server requires a JWT Bearer token issued by Andy.Auth when authentication is configured. The same RBAC permissions that gate the REST controllers also gate the corresponding MCP tools.

## Use Cases

- Ask Claude to review your codebase architecture.
- Search for patterns across repositories during a conversation.
- Generate documentation by having Claude read and summarize your code.
- Identify bugs by letting Claude analyze specific files with full context.
