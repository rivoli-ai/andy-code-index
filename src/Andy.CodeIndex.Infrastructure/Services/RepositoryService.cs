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
        var existingRepo = await _repositoryRepo.GetByUrlAsync(request.Url, ct);
        if (existingRepo is not null)
            throw new InvalidOperationException($"Repository with URL '{request.Url}' already exists.");

        if (!SyncIntervalValidator.IsValid(request.SyncIntervalMinutes))
            throw new ArgumentException($"Invalid sync interval value: {request.SyncIntervalMinutes}. Allowed values: null (default), 0 (manual only), 15, 30, 60, 120, 360, 720, 1440.");

        var provider = ParseProvider(request.Url);
        var name = ParseName(request.Url);

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = request.Url,
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
        var embeddingCount = await _context.ContentEmbeddings
            .CountAsync(ce => _context.Enrichments
                .Where(e => e.RepositoryId == id)
                .Select(e => e.Id)
                .Contains(ce.EnrichmentId), ct);

        dto.Stats = new RepositoryStatsDto
        {
            CommitCount = await _commitRepo.CountAsync(c => c.RepositoryId == id, ct),
            EnrichmentCount = await _enrichmentRepo.CountAsync(e => e.RepositoryId == id, ct),
            EmbeddingCount = embeddingCount,
            HasEmbeddings = embeddingCount > 0,
            PendingTaskCount = await _taskRepo.CountAsync(
                t => t.RepositoryId == id && t.Status == IndexingTaskStatus.Pending, ct)
        };

        return dto;
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
            var enrichmentCount = await _enrichmentRepo.CountAsync(e => e.RepositoryId == repo.Id, ct);
            var embeddingCount = await _context.ContentEmbeddings
                .CountAsync(ce => _context.Enrichments
                    .Where(e => e.RepositoryId == repo.Id)
                    .Select(e => e.Id)
                    .Contains(ce.EnrichmentId), ct);
            dto.Stats = new RepositoryStatsDto
            {
                EnrichmentCount = enrichmentCount,
                EmbeddingCount = embeddingCount,
                HasEmbeddings = embeddingCount > 0
            };
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

    private static RepositoryDto MapToDto(Repository repo)
    {
        return new RepositoryDto
        {
            Id = repo.Id,
            Name = repo.Name,
            Url = repo.Url,
            Provider = repo.Provider,
            DefaultBranch = repo.DefaultBranch,
            LastIndexedCommitSha = repo.LastIndexedCommitSha,
            LastSyncedAt = repo.LastSyncedAt,
            SyncIntervalMinutes = repo.SyncIntervalMinutes,
            Status = repo.Status,
            CreatedAt = repo.CreatedAt,
            UpdatedAt = repo.UpdatedAt
        };
    }
}
