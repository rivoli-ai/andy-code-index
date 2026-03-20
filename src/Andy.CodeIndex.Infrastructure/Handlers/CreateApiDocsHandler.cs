using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Handlers;

public class CreateApiDocsHandler : ITaskHandler
{
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly IEnrichmentRepository _enrichmentRepo;
    private readonly IGitService _gitService;
    private readonly ICodeAnalysisService _codeAnalysis;
    private readonly IndexingOptions _options;
    private readonly ILogger<CreateApiDocsHandler> _logger;

    public TaskOperation Operation => TaskOperation.CreatePublicAPIDocs;

    public CreateApiDocsHandler(
        ICodeRepositoryRepository repoRepo,
        IEnrichmentRepository enrichmentRepo,
        IGitService gitService,
        ICodeAnalysisService codeAnalysis,
        IOptions<IndexingOptions> options,
        ILogger<CreateApiDocsHandler> logger)
    {
        _repoRepo = repoRepo;
        _enrichmentRepo = enrichmentRepo;
        _gitService = gitService;
        _codeAnalysis = codeAnalysis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var repo = await _repoRepo.GetByIdAsync(task.RepositoryId, ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        // Delete old API docs
        await _enrichmentRepo.DeleteByRepositoryAndTypeAsync(repo.Id, EnrichmentType.Usage, ct: ct);

        var cloneDir = _gitService.GetCloneDir(_options.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);

        var docCount = 0;
        foreach (var file in files.Where(f => f.Language is not null && _codeAnalysis.SupportsLanguage(f.Language)))
        {
            var content = await _gitService.ReadFileAsync(cloneDir, commitSha, file.Path, ct);
            if (content is null) continue;

            var analysis = _codeAnalysis.Analyze(content, file.Path, file.Language!);
            if (analysis.Classes.Count == 0 && analysis.Interfaces.Count == 0 &&
                analysis.Functions.Count == 0 && analysis.Enums.Count == 0)
                continue;

            var apiDocs = _codeAnalysis.GenerateApiDocs(analysis);

            await _enrichmentRepo.AddAsync(new Enrichment
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Type = EnrichmentType.Usage,
                Subtype = EnrichmentSubtype.APIDocs,
                Title = $"API: {file.Path}",
                Content = apiDocs,
                FilePath = file.Path,
                Language = file.Language,
                CreatedAt = DateTime.UtcNow
            }, ct);
            docCount++;
        }

        await _enrichmentRepo.SaveChangesAsync(ct);
        _logger.LogInformation("Generated API docs for {Count} files in {Name}", docCount, repo.Name);
    }
}
