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

public class ExtractDocumentTextHandler : ITaskHandler
{
    private readonly CodeIndexDbContext _context;
    private readonly IGitService _gitService;
    private readonly IEnumerable<IDocumentParser> _parsers;
    private readonly IndexingOptions _indexingOptions;
    private readonly DocumentParsingOptions _documentParsingOptions;
    private readonly ILogger<ExtractDocumentTextHandler> _logger;

    // Extensions that document parsers can handle
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    public TaskOperation Operation => TaskOperation.ExtractDocumentText;

    public ExtractDocumentTextHandler(
        CodeIndexDbContext context,
        IGitService gitService,
        IEnumerable<IDocumentParser> parsers,
        IOptions<IndexingOptions> indexingOptions,
        IOptions<DocumentParsingOptions> documentParsingOptions,
        ILogger<ExtractDocumentTextHandler> logger)
    {
        _context = context;
        _gitService = gitService;
        _parsers = parsers;
        _indexingOptions = indexingOptions.Value;
        _documentParsingOptions = documentParsingOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(IndexingTask task, CancellationToken ct = default)
    {
        var trackedTask = await _context.IndexingTasks.FindAsync([task.Id], ct);
        if (trackedTask is not null)
        {
            trackedTask.ProgressMessage = "Extracting document text...";
            trackedTask.Progress = 0;
            await _context.SaveChangesAsync(ct);
        }

        if (!_documentParsingOptions.Enabled)
        {
            _logger.LogInformation("Document parsing is disabled globally, skipping");
            return;
        }

        var repo = await _context.Repositories.FindAsync([task.RepositoryId], ct)
            ?? throw new InvalidOperationException($"Repository {task.RepositoryId} not found");

        var cloneDir = _gitService.GetCloneDir(_indexingOptions.DataDir, repo.Id);
        var commitSha = repo.LastIndexedCommitSha ?? "HEAD";
        var files = await _gitService.ListFilesAsync(cloneDir, commitSha, ct: ct);

        // Filter to document files only
        var documentFiles = files
            .Where(f => DocumentExtensions.Contains(Path.GetExtension(f.Path)))
            .ToList();

        if (documentFiles.Count == 0)
        {
            _logger.LogInformation("No document files found in {Name}", repo.Name);
            return;
        }

        // Look up the commit record to set CommitId on enrichments
        var commitRecord = await _context.Commits
            .FirstOrDefaultAsync(c => c.RepositoryId == repo.Id && c.Sha == commitSha, ct);
        var commitId = commitRecord?.Id;

        // Build a lookup of previous file blob SHAs for skip-if-unchanged
        var previousFileHashes = new Dictionary<string, string>();
        if (commitRecord != null)
        {
            var previousCommitWithFiles = await _context.Commits
                .Where(c => c.RepositoryId == repo.Id && c.Id != commitRecord.Id)
                .OrderByDescending(c => c.CommittedAt)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (previousCommitWithFiles != Guid.Empty)
            {
                var prevFiles = await _context.RepositoryFiles
                    .Where(f => f.CommitId == previousCommitWithFiles)
                    .ToListAsync(ct);
                foreach (var pf in prevFiles)
                {
                    if (pf.Hash != null)
                        previousFileHashes[pf.Path] = pf.Hash;
                }
            }
        }

        // Load existing DocumentText enrichments for this repo
        var existingEnrichments = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id &&
                        e.Type == EnrichmentType.Development &&
                        e.Subtype == EnrichmentSubtype.DocumentText)
            .ToListAsync(ct);

        var existingByFilePath = existingEnrichments
            .GroupBy(e => e.FilePath ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        int added = 0, updated = 0, deleted = 0, skipped = 0;
        var processedFilePaths = new HashSet<string>();

        foreach (var file in documentFiles)
        {
            ct.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(file.Path);

            // Check per-format enablement
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && !_documentParsingOptions.Pdf.Enabled)
            {
                _logger.LogDebug("PDF parsing disabled, skipping {File}", file.Path);
                continue;
            }

            // Skip-if-unchanged: if blob SHA matches previous commit, skip this file
            if (file.Hash != null &&
                previousFileHashes.TryGetValue(file.Path, out var prevHash) &&
                prevHash == file.Hash)
            {
                skipped++;
                processedFilePaths.Add(file.Path);

                // Update CommitId on existing enrichments for skipped files
                if (existingByFilePath.TryGetValue(file.Path, out var skippedEnrichments))
                {
                    foreach (var se in skippedEnrichments)
                        se.CommitId = commitId;
                }

                _logger.LogDebug("Skipping document {File}: blob SHA unchanged since previous commit", file.Path);
                continue;
            }

            // Find a parser that can handle this file
            var parser = _parsers.FirstOrDefault(p => p.CanParse(ext));
            if (parser == null)
            {
                _logger.LogDebug("No parser found for extension {Ext}, skipping {File}", ext, file.Path);
                continue;
            }

            // Read file as binary
            var fileBytes = await _gitService.ReadFileBytesAsync(cloneDir, commitSha, file.Path, ct);
            if (fileBytes == null || fileBytes.Length == 0)
            {
                _logger.LogDebug("Empty or missing file {File}, skipping", file.Path);
                continue;
            }

            // Parse the document
            ParsedDocument parsedDoc;
            using (var stream = new MemoryStream(fileBytes))
            {
                parsedDoc = await parser.ParseAsync(stream, file.Path, ct);
            }

            if (string.IsNullOrWhiteSpace(parsedDoc.TextContent))
            {
                _logger.LogDebug("No text extracted from {File}, skipping", file.Path);
                continue;
            }

            processedFilePaths.Add(file.Path);

            // Remove old enrichments for this file (we recreate them)
            if (existingByFilePath.TryGetValue(file.Path, out var oldEnrichments))
            {
                foreach (var old in oldEnrichments)
                {
                    _context.Enrichments.Remove(old);
                    deleted++;
                }
            }

            // Create enrichments: one per section (page) for long docs, or one for short docs
            if (parsedDoc.Sections.Count > 1)
            {
                foreach (var section in parsedDoc.Sections)
                {
                    _context.Enrichments.Add(new Enrichment
                    {
                        Id = Guid.NewGuid(),
                        RepositoryId = repo.Id,
                        CommitId = commitId,
                        Type = EnrichmentType.Development,
                        Subtype = EnrichmentSubtype.DocumentText,
                        Title = $"{parsedDoc.Title ?? Path.GetFileName(file.Path)} - {section.Title ?? $"Page {section.PageNumber}"}",
                        Content = section.Content,
                        FilePath = file.Path,
                        StartLine = section.PageNumber,
                        EndLine = section.PageNumber,
                        Language = "pdf",
                        CreatedAt = DateTime.UtcNow
                    });
                    added++;
                }
            }
            else
            {
                // Single enrichment for the whole document
                _context.Enrichments.Add(new Enrichment
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repo.Id,
                    CommitId = commitId,
                    Type = EnrichmentType.Development,
                    Subtype = EnrichmentSubtype.DocumentText,
                    Title = parsedDoc.Title ?? Path.GetFileName(file.Path),
                    Content = parsedDoc.TextContent,
                    FilePath = file.Path,
                    Language = "pdf",
                    CreatedAt = DateTime.UtcNow
                });
                added++;
            }

            updated++; // Count the file as "updated" (old enrichments removed, new ones added)
        }

        // Delete enrichments for files that no longer exist in the repo
        foreach (var (filePath, enrichments) in existingByFilePath)
        {
            if (!processedFilePaths.Contains(filePath))
            {
                foreach (var orphan in enrichments)
                {
                    _context.Enrichments.Remove(orphan);
                    deleted++;
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Document text extraction for {Name}: {Added} enrichments added, {Updated} files updated, {Deleted} enrichments deleted, {Skipped} files skipped (unchanged blob SHA)",
            repo.Name, added, updated, deleted, skipped);
    }

    internal static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
    }
}
