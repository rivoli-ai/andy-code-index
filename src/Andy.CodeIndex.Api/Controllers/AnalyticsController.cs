using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.Rbac.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Api.Controllers;

[ApiController]
[Route("api/v1/repositories/{repositoryId:guid}/analytics")]
[Produces("application/json")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly CodeIndexDbContext _context;

    public AnalyticsController(CodeIndexDbContext context)
    {
        _context = context;
    }

    /// <summary>Get language breakdown for a repository.</summary>
    [RequirePermission("repository:read")]    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages(Guid repositoryId, CancellationToken ct = default)
    {
        var languages = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId &&
                        e.Subtype == EnrichmentSubtype.Chunk &&
                        e.Language != null)
            .GroupBy(e => e.Language!)
            .Select(g => new { language = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToListAsync(ct);

        return Ok(languages);
    }

    /// <summary>Get top terms from enrichment content.</summary>
    [RequirePermission("repository:read")]    [HttpGet("top-terms")]
    public async Task<IActionResult> GetTopTerms(Guid repositoryId, [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        // Get sample of chunk content for term extraction
        var chunks = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId &&
                        e.Subtype == EnrichmentSubtype.Chunk)
            .Select(e => e.Content)
            .Take(200)
            .ToListAsync(ct);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "shall", "can", "need", "dare", "ought",
            "used", "to", "of", "in", "for", "on", "with", "at", "by", "from",
            "as", "into", "through", "during", "before", "after", "above", "below",
            "between", "out", "off", "over", "under", "again", "further", "then",
            "once", "that", "this", "these", "those", "and", "but", "or", "nor",
            "not", "so", "if", "it", "its", "new", "get", "set", "var", "let",
            "const", "string", "int", "bool", "void", "null", "true", "false",
            "return", "public", "private", "protected", "static", "class", "interface",
            "using", "namespace", "async", "await", "task", "list", "dictionary",
            "import", "export", "from", "require", "module", "function", "def",
            "self", "none", "type", "any", "object", "readonly", "override",
        };

        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in chunks)
        {
            var words = content.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '(', ')', '{', '}', '[', ']', '<', '>', '/', '\\', '"', '\'', '=', '!', '?', '&', '|', '+', '-', '*', '@', '#', '$', '%', '^', '~', '`' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.Length < 3 || word.Length > 40) continue;
                if (stopWords.Contains(word)) continue;
                if (word.All(char.IsDigit)) continue;

                wordCounts.TryGetValue(word, out var count);
                wordCounts[word] = count + 1;
            }
        }

        var topTerms = wordCounts
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new { term = kv.Key, count = kv.Value })
            .ToList();

        return Ok(topTerms);
    }

    /// <summary>Get file type distribution.</summary>
    [RequirePermission("repository:read")]    [HttpGet("file-types")]
    public async Task<IActionResult> GetFileTypes(Guid repositoryId, CancellationToken ct = default)
    {
        var fileTypes = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId &&
                        e.Subtype == EnrichmentSubtype.Chunk &&
                        e.FilePath != null)
            .Select(e => e.FilePath!)
            .ToListAsync(ct);

        var extensions = fileTypes
            .Select(p => Path.GetExtension(p).ToLowerInvariant())
            .Where(ext => !string.IsNullOrEmpty(ext))
            .GroupBy(ext => ext)
            .Select(g => new { extension = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToList();

        return Ok(extensions);
    }

    /// <summary>Get files with most chunks (complexity indicator).</summary>
    [RequirePermission("repository:read")]    [HttpGet("complex-files")]
    public async Task<IActionResult> GetComplexFiles(Guid repositoryId, [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var files = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId &&
                        e.Subtype == EnrichmentSubtype.Chunk &&
                        e.FilePath != null)
            .GroupBy(e => e.FilePath!)
            .Select(g => new { filePath = g.Key, chunkCount = g.Count(), language = g.First().Language })
            .OrderByDescending(g => g.chunkCount)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(files);
    }

    /// <summary>Get comprehensive repository statistics.</summary>
    [RequirePermission("repository:read")]    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(Guid repositoryId, CancellationToken ct = default)
    {
        var repo = await _context.Repositories.FindAsync([repositoryId], ct);
        if (repo is null) return NotFound();

        var chunkCount = await _context.Enrichments.CountAsync(e => e.RepositoryId == repositoryId && e.Subtype == Domain.Enums.EnrichmentSubtype.Chunk, ct);
        var apiDocsCount = await _context.Enrichments.CountAsync(e => e.RepositoryId == repositoryId && e.Subtype == Domain.Enums.EnrichmentSubtype.APIDocs, ct);
        var embeddingCount = await _context.ContentEmbeddings.CountAsync(ce => _context.Enrichments.Where(e => e.RepositoryId == repositoryId).Select(e => e.Id).Contains(ce.EnrichmentId), ct);

        var enrichmentsByType = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId)
            .GroupBy(e => new { e.Type, e.Subtype })
            .Select(g => new { type = g.Key.Type.ToString(), subtype = g.Key.Subtype.ToString(), count = g.Count() })
            .ToListAsync(ct);

        var lastCommit = await _context.Commits
            .Where(c => c.RepositoryId == repositoryId)
            .OrderByDescending(c => c.CommittedAt)
            .FirstOrDefaultAsync(ct);

        var testFileCount = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId && e.Subtype == Domain.Enums.EnrichmentSubtype.Chunk && e.FilePath != null && (e.FilePath.Contains("Test") || e.FilePath.Contains("test") || e.FilePath.Contains(".spec.")))
            .Select(e => e.FilePath).Distinct().CountAsync(ct);

        var totalFiles = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId && e.Subtype == Domain.Enums.EnrichmentSubtype.Chunk && e.FilePath != null)
            .Select(e => e.FilePath).Distinct().CountAsync(ct);

        return Ok(new
        {
            repository = repo.Name,
            status = repo.Status,
            defaultBranch = repo.DefaultBranch,
            lastSyncedAt = repo.LastSyncedAt,
            lastCommit = lastCommit is not null ? new
            {
                sha = lastCommit.Sha,
                message = lastCommit.Message.Length > 100 ? lastCommit.Message[..100] + "..." : lastCommit.Message,
                authorName = lastCommit.AuthorName,
                authorEmail = lastCommit.AuthorEmail,
                committedAt = lastCommit.CommittedAt,
                age = DateTime.UtcNow - lastCommit.CommittedAt
            } : null,
            stats = new
            {
                totalFiles,
                testFiles = testFileCount,
                codeChunks = chunkCount,
                apiDocs = apiDocsCount,
                embeddings = embeddingCount,
                hasEmbeddings = embeddingCount > 0,
            },
            enrichmentsByType
        });
    }
}
