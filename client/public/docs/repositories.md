# Repositories

Repositories are the core data source for CodeIndex. Once added, files are parsed, indexed, and made available for search and enrichment.

## Adding a Repository

Navigate to **Repositories > Add Repository** and provide:

- **URL** -- HTTPS or SSH clone URL.
- **Branch** -- The branch to index. Defaults to the default branch.
- **Access token** -- Required for private repositories.

Click **Add** to begin cloning. The initial sync runs automatically.

## Repository Dashboard

Each repository has a detail page showing:

- **File count** -- Total indexed files.
- **Embedding status** -- How many files have vector embeddings.
- **Last sync** -- Timestamp of the most recent synchronization.
- **Language breakdown** -- Distribution of programming languages.

## Syncing

### Automatic Sync

Repositories sync on a configurable interval (see [Settings](settings)). Each sync pulls the latest changes and re-indexes modified files.

### Manual Sync

Click the **Sync** button on a repository detail page to trigger an immediate sync. This is useful after pushing new commits.

### Incremental Updates

Only changed files are re-processed during sync. Unchanged files retain their existing embeddings, keeping sync fast.

## File Filters

You can exclude files from indexing using glob patterns:

- `**/node_modules/**` -- Skip dependency directories.
- `**/*.min.js` -- Skip minified files.
- `**/dist/**` -- Skip build output.

Filters are configured per repository on the detail page.

## Deleting a Repository

Click **Delete** on the repository detail page. This removes all indexed data, embeddings, and enrichments associated with the repository.

Deletion is permanent and cannot be undone.

## Organization Grouping

Repositories are automatically grouped by organization or owner. The repository list view shows these groups with collapsible sections for easier navigation.

## Supported Providers

CodeIndex works with any Git-compatible hosting provider:

- GitHub (public and private)
- GitLab (self-hosted or cloud)
- Bitbucket
- Azure DevOps

## Best Practices

- Index only branches you actively develop on.
- Use file filters to exclude generated code and dependencies.
- Schedule syncs to match your team's commit frequency.
- Monitor the Tasks page for sync errors.
