# Search

CodeIndex provides three search modes to help you find code across all indexed repositories.

## Semantic Search

Semantic search uses vector embeddings to find code by meaning rather than exact text matches. It understands natural language queries.

### Example Queries

- "error handling in API controllers"
- "database connection pooling logic"
- "user authentication flow"

Semantic search requires an embedding key configured in [Settings](settings).

## Keyword Search (BM25)

Keyword search uses the BM25 algorithm to match documents by term frequency. It works well for finding exact identifiers, function names, or specific strings.

### Example Queries

- `handleError`
- `ConnectionPool`
- `JWT_SECRET`

Keyword search is always available and does not require an embedding key.

## Hybrid Search

Hybrid search combines semantic and keyword results, giving you the best of both approaches. Results are scored using Reciprocal Rank Fusion (RRF), which merges the two ranked lists into a single ordering.

Toggle hybrid mode using the search controls above the results list.

## Search Controls

### Repository Filter

Limit results to a specific repository using the repository dropdown. By default, search runs across all indexed repositories.

### Language Filter

Filter results by programming language to narrow down matches.

### Result Count

Adjust the number of results returned (default is 20).

## Understanding Results

Each result shows:

- **File path** -- The full path within the repository.
- **Repository** -- Which repository the file belongs to.
- **Score** -- Relevance score (higher is better).
- **Code preview** -- A snippet of the matching code with syntax highlighting.

Click a result to view the full file with the match highlighted.

## Search Tips

- Use natural language for semantic search -- describe what the code does, not what it looks like.
- Use exact identifiers for keyword search -- function names, class names, constants.
- Combine both with hybrid mode when you are not sure which approach fits best.
- Apply repository and language filters to reduce noise in large codebases.

## API Access

Search is also available via the REST API:

```bash
curl -k -X POST https://localhost:7101/api/v1/search \
  -H "Content-Type: application/json" \
  -d '{"query": "authentication middleware", "mode": "hybrid", "limit": 10}'
```

See [API Reference](api-reference) for the full specification.
