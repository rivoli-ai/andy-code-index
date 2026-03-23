using System.Security.Cryptography;
using System.Text;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class ExtractSnippetsHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IChunkingService _chunkingService;
    private readonly IndexingOptions _indexingOptions;
    private readonly ILogger<ExtractSnippetsHandler> _logger;

    public TaskOperation Operation => TaskOperation.ExtractSnippets;

    public ExtractSnippetsHandler(
        CodeIndexDbContext context,
        IGitService gitService,
        IChunkingService chunkingService,
        IOptions<IndexingOptions> indexingOptions,
        ILogger<ExtractSnippetsHandler> logger)
    {
        _context = context;
        _gitService = gitService;
        _chunkingService = chunkingService;
        _indexingOptions = indexingOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);

        // Build new chunks from current file state
        var newChunks = new List<(string filePath, string language, CodeChunk chunk, string contentHash)>();
        foreach (var file in files.Where(f => f.Language is not null))
        {
            var content = await _gitService.ReadFileAsync(cloneDir, commitSha, file.Path, ct);
            if (content is null || content.Length == 0) continue;

            var chunks = _chunkingService.ChunkText(content, file.Path);
            foreach (var chunk in chunks)
            {
                newChunks.Add((file.Path, file.Language!, chunk, ComputeHash(chunk.Content)));
            }
        }

        // Load existing chunk enrichments for this repo
        var existingChunks = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id &&
                        e.Type == EnrichmentType.Development &&
                        e.Subtype == EnrichmentSubtype.Chunk)
            .ToListAsync(ct);

        // Build lookup: (filePath, startLine, endLine) → existing enrichment
        var existingByKey = new Dictionary<string, Enrichment>();
        foreach (var e in existingChunks)
            existingByKey[$"{e.FilePath}:{e.StartLine}:{e.EndLine}"] = e;

        int added = 0, updated = 0, deleted = 0, unchanged = 0;

        // Process new chunks: add or update
        var processedKeys = new HashSet<string>();
        foreach (var (filePath, language, chunk, contentHash) in newChunks)
        {
            var key = $"{filePath}:{chunk.StartLine}:{chunk.EndLine}";
            processedKeys.Add(key);

            if (existingByKey.TryGetValue(key, out var existing))
            {
                var existingHash = ComputeHash(existing.Content);
                if (existingHash != contentHash)
                {
                    // Modified — update content, preserve ID and embeddings
                    existing.Content = chunk.Content;
                    existing.Language = language;
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }
            else
            {
                // New chunk
                _context.Enrichments.Add(new Enrichment
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repo.Id,
                    Type = EnrichmentType.Development,
                    Subtype = EnrichmentSubtype.Chunk,
                    Content = chunk.Content,
                    FilePath = chunk.FilePath,
                    StartLine = chunk.StartLine,
                    EndLine = chunk.EndLine,
                    Language = language,
                    CreatedAt = DateTime.UtcNow
                });
                added++;
            }
        }

        // Delete chunks that no longer exist (file removed or chunk boundaries changed)
        foreach (var existing in existingChunks)
        {
            var key = $"{existing.FilePath}:{existing.StartLine}:{existing.EndLine}";
            if (!processedKeys.Contains(key))
            {
                _context.Enrichments.Remove(existing);
                deleted++;
            }
        }

        // Record indexing run stats
        _context.IndexingRuns.Add(new IndexingRun
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            ChainId = task.ChainId,
            StartedAt = task.StartedAt ?? DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = "completed",
            SnippetsAdded = added,
            SnippetsUpdated = updated,
            SnippetsDeleted = deleted,
            SnippetsUnchanged = unchanged,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);

        // Emit telemetry
        Telemetry.CodeIndexTelemetry.SnippetsAdded.Add(added, new KeyValuePair<string, object?>("repository", repo.Name));
        Telemetry.CodeIndexTelemetry.SnippetsUpdated.Add(updated, new KeyValuePair<string, object?>("repository", repo.Name));
        Telemetry.CodeIndexTelemetry.SnippetsDeleted.Add(deleted, new KeyValuePair<string, object?>("repository", repo.Name));
        Telemetry.CodeIndexTelemetry.SnippetsUnchanged.Add(unchanged, new KeyValuePair<string, object?>("repository", repo.Name));

        repo.Status = "indexing";
        repo.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Snippets for {Name}: {Added} added, {Updated} updated, {Deleted} deleted, {Unchanged} unchanged",
            repo.Name, added, updated, deleted, unchanged);
    }

    internal static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
    }
}
