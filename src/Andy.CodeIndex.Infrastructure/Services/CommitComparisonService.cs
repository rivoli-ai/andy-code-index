using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;

namespace Andy.CodeIndex.Infrastructure.Services;

public class CommitComparisonService : ICommitComparisonService
{
    private readonly ICommitRepository _commitRepo;
    private readonly IEnrichmentRepository _enrichmentRepo;

    public CommitComparisonService(
        ICommitRepository commitRepo,
        IEnrichmentRepository enrichmentRepo)
    {
        _commitRepo = commitRepo;
        _enrichmentRepo = enrichmentRepo;
    }

    public async Task<CommitComparisonDto?> CompareAsync(
        Guid repositoryId, string fromSha, string toSha, CancellationToken ct = default)
    {
        var fromCommit = await _commitRepo.GetByShaAsync(repositoryId, fromSha, ct);
        var toCommit = await _commitRepo.GetByShaAsync(repositoryId, toSha, ct);

        if (fromCommit is null || toCommit is null)
            return null;

        var fromEnrichments = await _enrichmentRepo.GetByCommitIdAsync(fromCommit.Id, ct);
        var toEnrichments = await _enrichmentRepo.GetByCommitIdAsync(toCommit.Id, ct);

        return Compare(fromSha, toSha, fromEnrichments, toEnrichments);
    }

    internal static CommitComparisonDto Compare(
        string fromSha, string toSha,
        List<Enrichment> fromEnrichments,
        List<Enrichment> toEnrichments)
    {
        var result = new CommitComparisonDto { From = fromSha, To = toSha };

        // Build lookup by identity key: FilePath + Subtype
        var fromByKey = new Dictionary<string, Enrichment>();
        foreach (var e in fromEnrichments)
        {
            var key = BuildKey(e);
            fromByKey.TryAdd(key, e);
        }

        var toByKey = new Dictionary<string, Enrichment>();
        foreach (var e in toEnrichments)
        {
            var key = BuildKey(e);
            toByKey.TryAdd(key, e);
        }

        // Find added and changed
        foreach (var (key, toEnrichment) in toByKey)
        {
            if (fromByKey.TryGetValue(key, out var fromEnrichment))
            {
                // Exists in both -- check if content changed
                if (fromEnrichment.Content != toEnrichment.Content)
                {
                    result.Changed.Add(new EnrichmentChangeDto
                    {
                        From = MapToDto(fromEnrichment),
                        To = MapToDto(toEnrichment)
                    });
                }
            }
            else
            {
                // Only in 'to' -- added
                result.Added.Add(MapToDto(toEnrichment));
            }
        }

        // Find removed (in 'from' but not in 'to')
        foreach (var (key, fromEnrichment) in fromByKey)
        {
            if (!toByKey.ContainsKey(key))
            {
                result.Removed.Add(MapToDto(fromEnrichment));
            }
        }

        return result;
    }

    private static string BuildKey(Enrichment e)
        => $"{e.FilePath ?? ""}|{e.Subtype}";

    private static EnrichmentDto MapToDto(Enrichment e) => new()
    {
        Id = e.Id,
        RepositoryId = e.RepositoryId,
        CommitId = e.CommitId,
        Type = e.Type,
        Subtype = e.Subtype,
        Title = e.Title,
        Content = e.Content,
        FilePath = e.FilePath,
        StartLine = e.StartLine,
        EndLine = e.EndLine,
        Language = e.Language,
        Quality = e.Quality,
        CreatedAt = e.CreatedAt
    };
}
