# Settings

The Settings page lets you configure API keys, embedding providers, LLM providers, and application behavior.

## Embedding Provider

The embedding provider generates vector representations of your code for semantic search.

### Supported Providers

- **OpenAI** -- `https://api.openai.com/v1` (default)
- **Ollama** -- `http://localhost:11434/v1` (local, free)
- **Groq** -- `https://api.groq.com/openai/v1`
- **Azure OpenAI** -- Your Azure endpoint URL

### Configuration

1. Enter the provider's base URL.
2. Enter your API key.
3. Select the embedding model (e.g., `text-embedding-3-small`).
4. Click **Save**.

### Model Selection

Choose a model that balances quality and cost:

- `text-embedding-3-small` -- Fast, lower cost, good quality.
- `text-embedding-3-large` -- Higher quality, higher cost.
- `nomic-embed-text` -- Open source, works with Ollama.

## LLM Provider

The LLM provider powers enrichments, insights, and chat features.

### Supported Providers

- **OpenAI** -- GPT-4, GPT-4o
- **Anthropic** -- Claude 3.5 Sonnet, Claude 3 Opus
- **Ollama** -- Local models (Llama, Mistral, etc.)
- **Any OpenAI-compatible API**

### Configuration

1. Enter the provider's base URL.
2. Enter your API key.
3. Select the model.
4. Click **Save**.

## Sync Intervals

Configure how often repositories are automatically synchronized:

- **Manual only** -- No automatic sync.
- **Every hour** -- Good for active development.
- **Every 6 hours** -- Balanced approach.
- **Daily** -- Low-traffic repositories.

## File Size Limits

Set the maximum file size for indexing. Files larger than this limit are skipped during sync. Default is 1 MB.

## Embedding Dimensions

Configure the vector dimensions for embeddings. This must match your embedding model's output dimensions:

- `text-embedding-3-small` -- 1536 dimensions
- `text-embedding-3-large` -- 3072 dimensions

Changing dimensions requires re-embedding all repositories.

## Key Management

### Viewing Keys

Configured keys are shown in masked form (e.g., `sk-...abc123`). The source indicates whether the key was set via the UI or environment variables.

### Removing Keys

Click **Remove** next to a key to delete it. Environment-variable keys cannot be removed from the UI.

### Priority

Keys set via the UI take precedence over environment variables. Remove a UI key to fall back to the environment variable.

## Resetting Settings

To reset all settings to defaults, clear the application data or restart with a fresh database.
