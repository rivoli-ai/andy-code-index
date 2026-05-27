# Getting Started

Welcome to CodeIndex, a semantic code search and intelligence platform. This guide walks you through initial setup, indexing your first repository, and running your first search.

## Prerequisites

Before you begin, make sure you have:

- Docker and Docker Compose installed
- At least 4 GB of RAM available for containers
- A GitHub personal access token (for private repositories)

## Starting the Application

Clone the repository and start all services with Docker Compose:

```bash
git clone https://github.com/rivoli-ai/andy-code-index.git
cd andy-code-index
cp .env.example .env
docker compose up -d
```

In Docker, the web client is available on `https://localhost:6201` and the API on `https://localhost:7101` by default. For local development outside Docker, the Angular dev server runs on `https://localhost:4201` and the .NET API on `https://localhost:5101`.

## Configuring an Embedding Key

Navigate to **Settings** and enter your OpenAI API key (or compatible provider). This enables semantic search by generating vector embeddings for your code.

Without an embedding key, only keyword-based (BM25) search is available.

## Adding Your First Repository

1. Go to **Repositories** and click **Add Repository**.
2. Enter the repository URL (HTTPS or SSH).
3. Choose a branch to index (defaults to the main branch).
4. Click **Add** to start cloning and indexing.

The initial sync will clone the repository, parse all files, and generate embeddings. Progress is visible on the **Tasks** page.

## Running Your First Search

Once indexing completes:

1. Navigate to **Search**.
2. Type a natural language query, such as "authentication middleware".
3. Results are ranked by semantic similarity to your query.

You can toggle between semantic search, keyword search, or hybrid mode using the search controls.

## Next Steps

- [Repositories](repositories) -- Learn about sync options and filters.
- [Search](search) -- Explore hybrid search and advanced queries.
- [Enrichments](enrichments) -- Generate LLM-powered documentation for your code.
- [Chat](chat) -- Ask questions about your codebase in natural language.

## Troubleshooting

### Containers fail to start

Check that ports 6201, 7101, 7102, and 7436 are not in use. Review logs with:

```bash
docker compose logs -f
```

### Embeddings are not generated

Verify your API key in Settings. Ensure the embedding provider URL is reachable from the Docker network.

### Repository sync is stuck

Check the Tasks dashboard for error details. Common causes include invalid credentials or network timeouts.
