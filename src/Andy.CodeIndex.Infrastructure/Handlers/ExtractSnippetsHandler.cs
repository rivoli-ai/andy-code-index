using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class ExtractSnippetsHandler : ITaskHandler
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly IEnrichmentRepository _enrichmentRepo;
    private readonly IGitService _gitService;
    private readonly IChunkingService _chunkingService;
    private readonly IndexingOptions _indexingOptions;
    private readonly ILogger<ExtractSnippetsHandler> _logger;

    public TaskOperation Operation => TaskOperation.ExtractSnippets;

    public ExtractSnippetsHandler(
        ICodeRepositoryRepository repoRepo,
        IEnrichmentRepository enrichmentRepo,
        IGitService gitService,
        IChunkingService chunkingService,
        IOptions<IndexingOptions> indexingOptions,
        ILogger<ExtractSnippetsHandler> logger)
    {
        _repoRepo = repoRepo;
        _enrichmentRepo = enrichmentRepo;
        _gitService = gitService;
        _chunkingService = chunkingService;
        _indexingOptions = indexingOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(task.RepositoryId, ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        // Delete existing chunks to prevent duplication on re-index
        await _enrichmentRepo.DeleteByRepositoryAndTypeAsync(repo.Id, EnrichmentType.Development, ct: ct);
        _logger.LogInformation("Cleared existing Development enrichments for {Name}", repo.Name);

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);

        var totalChunks = 0;
        foreach (var file in files.Where(f => f.Language is not null))
        {
            var content = await _gitService.ReadFileAsync(cloneDir, commitSha, file.Path, ct);
            if (content is null || content.Length == 0) continue;

            var chunks = _chunkingService.ChunkText(content, file.Path);
            foreach (var chunk in chunks)
            {
                await _enrichmentRepo.AddAsync(new Enrichment
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repo.Id,
                    Type = EnrichmentType.Development,
                    Subtype = EnrichmentSubtype.Chunk,
                    Content = chunk.Content,
                    FilePath = chunk.FilePath,
                    StartLine = chunk.StartLine,
                    EndLine = chunk.EndLine,
                    Language = file.Language,
                    CreatedAt = DateTime.UtcNow
                }, ct);
                totalChunks++;
            }
        }

        await _enrichmentRepo.SaveChangesAsync(ct);

        repo.Status = "indexing";
        repo.UpdatedAt = DateTime.UtcNow;
        _repoRepo.Update(repo);
        await _repoRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Extracted {Chunks} snippets from {Files} files for {Name}",
            totalChunks, files.Count, repo.Name);
    }
}
