using System.Text.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Services;

public class RepositoryService : IRepositoryService
{
    private readonly ICodeRepositoryRepository _repositoryRepo;
    private readonly ICommitRepository _commitRepo;
    private readonly IEnrichmentRepository _enrichmentRepo;
    private readonly IIndexingTaskRepository _taskRepo;
    private readonly CodeIndexDbContext _context;

    public RepositoryService(
        ICodeRepositoryRepository repositoryRepo,
        ICommitRepository commitRepo,
        IEnrichmentRepository enrichmentRepo,
        IIndexingTaskRepository taskRepo,
        CodeIndexDbContext context)
    {
        _repositoryRepo = repositoryRepo;
        _commitRepo = commitRepo;
        _enrichmentRepo = enrichmentRepo;
        _taskRepo = taskRepo;
        _context = context;
    }

    public async Task<RepositoryDto> AddAsync(CreateRepositoryRequest request, CancellationToken ct = default)
    {
        var normalizedUrl = NormalizeUrl(request.Url);

        var existingRepo = await _repositoryRepo.GetByUrlAsync(normalizedUrl, ct);
        if (existingRepo is not null)
            throw new InvalidOperationException($"Repository with URL '{normalizedUrl}' already exists.|{existingRepo.Id}");

        if (!SyncIntervalValidator.IsValid(request.SyncIntervalMinutes))
            throw new ArgumentException($"Invalid sync interval value: {request.SyncIntervalMinutes}. Allowed values: null (default), 0 (manual only), 15, 30, 60, 120, 360, 720, 1440.");

        var provider = ParseProvider(normalizedUrl);
        var name = ParseName(normalizedUrl);

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = normalizedUrl,
            CloneUrl = request.Url,
            Provider = provider,
            PersonalAccessToken = request.PersonalAccessToken,
            SyncIntervalMinutes = request.SyncIntervalMinutes,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repositoryRepo.AddAsync(repo, ct);
        await _repositoryRepo.SaveChangesAsync(ct);

        // Queue initial clone + index chain
        var chainId = Guid.NewGuid();
        await _taskRepo.AddAsync(new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Operation = TaskOperation.CloneRepository,
            Status = IndexingTaskStatus.Pending,
            ChainId = chainId,
            Priority = 10,
            CreatedAt = DateTime.UtcNow
        }, ct);
        await _taskRepo.SaveChangesAsync(ct);

        return MapToDto(repo);
    }

    public async Task<RepositoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await _repositoryRepo.GetByIdAsync(id, ct);
        return repo is null ? null : MapToDto(repo);
    }

    public async Task<RepositoryDto?> GetDetailsByIdAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await _repositoryRepo.GetWithBranchesAndTagsAsync(id, ct);
        if (repo is null) return null;

        var dto = MapToDto(repo);
        dto.Branches = repo.Branches.Select(b => new BranchDto
        {
            Name = b.Name,
            HeadCommitSha = b.HeadCommitSha,
            IsDefault = b.IsDefault
        }).ToList();
        dto.Tags = repo.Tags.Select(t => new TagDto
        {
            Name = t.Name,
            CommitSha = t.CommitSha
        }).ToList();
        dto.Stats = await BuildStatsAsync(repo, ct);

        return dto;
    }

    /// <summary>
    /// Computes the full statistics for a repository. Shared by <see cref="GetDetailsByIdAsync"/>
    /// and <see cref="ListAsync"/> so both endpoints report the same numbers.
    /// </summary>
    private async Task<RepositoryStatsDto> BuildStatsAsync(Repository repo, CancellationToken ct)
    {
        var id = repo.Id;

        var enrichmentCount = await _enrichmentRepo.CountAsync(e => e.RepositoryId == id, ct);
        var embeddingCount = await _context.ContentEmbeddings
            .CountAsync(ce => _context.Enrichments
                .Where(e => e.RepositoryId == id)
                .Select(e => e.Id)
                .Contains(ce.EnrichmentId), ct);
        var storageSizeBytes = await _context.Enrichments
            .Where(e => e.RepositoryId == id)
            .SumAsync(e => (long)e.Content.Length, ct);
        var commitCount = await _commitRepo.CountAsync(c => c.RepositoryId == id, ct);
        // RepositoryFiles link to a Commit, which links to the repository.
        var fileCount = await _context.RepositoryFiles
            .CountAsync(f => f.Commit.RepositoryId == id, ct);
        var pendingTaskCount = await _taskRepo.CountAsync(
            t => t.RepositoryId == id && t.Status == IndexingTaskStatus.Pending, ct);
        var hasInsights = await _context.Enrichments
            .AnyAsync(e => e.RepositoryId == id && e.Type == EnrichmentType.Insights, ct);

        // Compute attention indicators
        var needsAttention = false;
        string? attentionReason = null;
        if (enrichmentCount == 0)
        { needsAttention = true; attentionReason = "Not yet indexed"; }
        else if (repo.Status is "error" or "cloned")
        { needsAttention = true; attentionReason = $"Status: {repo.Status}"; }
        else if (repo.LastSyncedAt.HasValue && (DateTime.UtcNow - repo.LastSyncedAt.Value).TotalDays > 7 && repo.SyncIntervalMinutes != 0)
        { needsAttention = true; attentionReason = "Last sync over 7 days ago"; }
        else if (enrichmentCount > 0 && !hasInsights)
        { needsAttention = true; attentionReason = "No insights generated"; }

        return new RepositoryStatsDto
        {
            CommitCount = commitCount,
            FileCount = fileCount,
            EnrichmentCount = enrichmentCount,
            StorageSizeBytes = storageSizeBytes,
            EmbeddingCount = embeddingCount,
            HasEmbeddings = embeddingCount > 0,
            PendingTaskCount = pendingTaskCount,
            NeedsAttention = needsAttention,
            AttentionReason = attentionReason,
            HasInsights = hasInsights
        };
    }

    public async Task<List<RepositoryDto>> ListAsync(GitProvider? provider = null, string? status = null, CancellationToken ct = default)
    {
        List<Repository> repos;

        if (provider.HasValue)
            repos = await _repositoryRepo.GetByProviderAsync(provider.Value, ct);
        else if (status is not null)
            repos = await _repositoryRepo.GetByStatusAsync(status, ct);
        else
            repos = await _repositoryRepo.GetAllAsync(ct);

        var dtos = new List<RepositoryDto>();
        foreach (var repo in repos)
        {
            var dto = MapToDto(repo);
            dto.Stats = await BuildStatsAsync(repo, ct);
            dtos.Add(dto);
        }
        return dtos;
    }

    public async Task<RepositoryDto> UpdateAsync(Guid id, UpdateRepositoryRequest request, CancellationToken ct = default)
    {
        var repo = await _repositoryRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Repository {id} not found.");

        if (!SyncIntervalValidator.IsValid(request.SyncIntervalMinutes))
            throw new ArgumentException($"Invalid sync interval value: {request.SyncIntervalMinutes}. Allowed values: null (default), 0 (manual only), 15, 30, 60, 120, 360, 720, 1440.");

        repo.SyncIntervalMinutes = request.SyncIntervalMinutes;

        if (request.FileFilterOverrides is not null)
        {
            repo.FileFilterOverrides = JsonSerializer.Serialize(request.FileFilterOverrides,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        repo.UpdatedAt = DateTime.UtcNow;

        await _repositoryRepo.SaveChangesAsync(ct);
        return MapToDto(repo);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await _repositoryRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Repository {id} not found.");

        _repositoryRepo.Remove(repo);
        await _repositoryRepo.SaveChangesAsync(ct);
    }

    public async Task SyncAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await _repositoryRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Repository {id} not found.");

        // Block duplicate sync if tasks are already pending or running for this repo
        var existingTasks = await _taskRepo.GetByRepositoryAsync(repo.Id, ct);
        var hasActiveTasks = existingTasks.Any(t =>
            t.Status is IndexingTaskStatus.Pending or IndexingTaskStatus.Running);
        if (hasActiveTasks)
            throw new InvalidOperationException($"Repository '{repo.Name}' already has active tasks. Wait for them to complete before syncing again.");

        var chainId = Guid.NewGuid();
        await _taskRepo.AddAsync(new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Operation = TaskOperation.SyncRepository,
            Status = IndexingTaskStatus.Pending,
            ChainId = chainId,
            Priority = 5,
            CreatedAt = DateTime.UtcNow
        }, ct);
        await _taskRepo.SaveChangesAsync(ct);
    }

    public async Task WipeEnrichmentsAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await _repositoryRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Repository {id} not found.");

        // Block wipe if active tasks exist
        var existingTasks = await _taskRepo.GetByRepositoryAsync(repo.Id, ct);
        var hasActiveTasks = existingTasks.Any(t =>
            t.Status is IndexingTaskStatus.Pending or IndexingTaskStatus.Running);
        if (hasActiveTasks)
            throw new InvalidOperationException("Cannot wipe: active tasks exist for this repository. Cancel them first.");

        // Delete all enrichments and related data
        var enrichmentIds = await _context.Enrichments
            .Where(e => e.RepositoryId == id)
            .Select(e => e.Id)
            .ToListAsync(ct);

        if (enrichmentIds.Count > 0)
        {
            // Delete content embeddings first (FK)
            await _context.ContentEmbeddings
                .Where(ce => enrichmentIds.Contains(ce.EnrichmentId))
                .ExecuteDeleteAsync(ct);

            // Delete enrichments
            await _context.Enrichments
                .Where(e => e.RepositoryId == id)
                .ExecuteDeleteAsync(ct);
        }

        // Reset repo status
        repo.Status = "cloned";
        await _repositoryRepo.SaveChangesAsync(ct);

        // Enrichments wiped successfully
    }

    public async Task<RepositoryDto?> FindByUrlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeUrl(url);
            var repo = await _repositoryRepo.GetByUrlAsync(normalized, ct);
            return repo is null ? null : MapToDto(repo);
        }
        catch
        {
            return null;
        }
    }

    public async Task<StorageStatsDto> GetStorageStatsAsync(Guid repositoryId, CancellationToken ct = default)
    {
        var byType = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId)
            .GroupBy(e => e.Subtype)
            .Select(g => new StorageByTypeDto
            {
                Type = g.Key.ToString(),
                Count = g.Count(),
                SizeBytes = g.Sum(e => (long)e.Content.Length)
            })
            .ToListAsync(ct);

        return new StorageStatsDto
        {
            TotalEnrichments = byType.Sum(b => b.Count),
            TotalSizeBytes = byType.Sum(b => b.SizeBytes),
            ByType = byType
        };
    }

    public async Task<StorageStatsDto> GetGlobalStorageStatsAsync(CancellationToken ct = default)
    {
        var byType = await _context.Enrichments
            .GroupBy(e => e.Subtype)
            .Select(g => new StorageByTypeDto
            {
                Type = g.Key.ToString(),
                Count = g.Count(),
                SizeBytes = g.Sum(e => (long)e.Content.Length)
            })
            .ToListAsync(ct);

        return new StorageStatsDto
        {
            TotalEnrichments = byType.Sum(b => b.Count),
            TotalSizeBytes = byType.Sum(b => b.SizeBytes),
            ByType = byType
        };
    }

    internal static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');

        // Remove .git suffix for consistent comparison
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        // HTTP and HTTPS are repository-location aliases for supported hosted
        // providers. Keep non-default ports because they distinguish self-hosted
        // Git servers that share a hostname.
        var uri = new Uri(trimmed);
        var scheme = uri.Scheme is "http" or "https"
            ? Uri.UriSchemeHttps
            : uri.Scheme.ToLowerInvariant();
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6)
            host = $"[{host}]";
        var authority = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";

        return $"{scheme}://{authority}{uri.AbsolutePath.TrimEnd('/')}";
    }

    internal static GitProvider ParseProvider(string url)
    {
        var uri = new Uri(url);
        var host = uri.Host.ToLowerInvariant();

        if (host.Contains("github.com") || host.Contains("github"))
            return GitProvider.GitHub;
        if (host.Contains("gitlab.com") || host.Contains("gitlab"))
            return GitProvider.GitLab;
        if (host.Contains("gitea"))
            return GitProvider.Gitea;
        if (host.Contains("dev.azure.com") || host.Contains("visualstudio.com") || host.Contains("azure"))
            return GitProvider.AzureDevOps;

        return GitProvider.GitHub; // Default fallback
    }

    internal static string ParseName(string url)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath.TrimEnd('/');

        // Remove .git suffix
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];

        // Azure DevOps: /org/project/_git/repo
        if (path.Contains("/_git/"))
        {
            var gitIndex = path.IndexOf("/_git/", StringComparison.Ordinal);
            return path[(gitIndex + 6)..];
        }

        // Standard: /owner/repo → return "repo"
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
    }

    internal static string? ParseOrganization(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.TrimEnd('/');

            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                path = path[..^4];

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Azure DevOps: /org/project/_git/repo → org is first segment
            if (path.Contains("/_git/") && segments.Length >= 1)
                return segments[0];

            // Standard (GitHub/GitLab/Gitea): /owner/repo → owner is first segment
            if (segments.Length >= 2)
                return segments[0];

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static RepositoryDto MapToDto(Repository repo)
    {
        FileFilterOverridesDto? filterOverrides = null;
        if (repo.FileFilterOverrides is not null)
        {
            try
            {
                filterOverrides = JsonSerializer.Deserialize<FileFilterOverridesDto>(
                    repo.FileFilterOverrides,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                // Invalid JSON — leave null
            }
        }

        return new RepositoryDto
        {
            Id = repo.Id,
            Name = repo.Name,
            Url = repo.Url,
            Organization = ParseOrganization(repo.Url),
            Provider = repo.Provider,
            DefaultBranch = repo.DefaultBranch,
            LastIndexedCommitSha = repo.LastIndexedCommitSha,
            LastSyncedAt = repo.LastSyncedAt,
            SyncIntervalMinutes = repo.SyncIntervalMinutes,
            Status = repo.Status,
            FileFilterOverrides = filterOverrides,
            CreatedAt = repo.CreatedAt,
            UpdatedAt = repo.UpdatedAt
        };
    }
}
