# Enrichments

Enrichments are LLM-generated documentation and metadata attached to your indexed code. They provide human-readable summaries, explanations, and structural analysis for files and functions.

## What Are Enrichments?

When you generate enrichments for a repository, CodeIndex sends code to an LLM and stores the generated documentation alongside the original files. This makes code easier to search, understand, and discuss.

## Enrichment Types

### File Summaries

A concise description of what each file does, its main exports, and its role in the project.

### Function Documentation

Per-function documentation including:

- Purpose and behavior
- Parameter descriptions
- Return value explanations
- Side effects and dependencies

### Architecture Notes

High-level observations about patterns, design decisions, and how components interact.

## Generating Enrichments

1. Navigate to **Enrichments**.
2. Select a repository.
3. Choose the enrichment type to generate.
4. Click **Generate** to start processing.

Progress is tracked on the **Tasks** page. Large repositories may take several minutes depending on file count and LLM response times.

## LLM Configuration

Enrichments require an LLM key configured in [Settings](settings). Supported providers include:

- OpenAI (GPT-4, GPT-4o)
- Anthropic (Claude)
- Local models via Ollama

The LLM key is separate from the embedding key. You can use different providers for each.

## Browsing Enrichments

The Enrichments page displays generated documentation organized by repository and file. Use the search bar to filter by file name or content.

Each enrichment shows:

- The original file path
- Generation timestamp
- The enrichment content with formatted markdown

## Re-generating Enrichments

When code changes after a sync, enrichments can become stale. Re-generate enrichments for updated files to keep documentation current.

Stale enrichments are flagged in the UI with a warning indicator.

## Cost Considerations

Enrichment generation sends code to an external LLM API. Costs depend on:

- Number of files processed
- Average file size
- The LLM provider's pricing

Start with a small repository to estimate costs before processing large codebases.
