using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Andy.CodeIndex.Infrastructure.Discovery;

public class RepoDiscoveryService : IRepoDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICodeRepositoryRepository _repoRepo;
    private readonly ILogger<RepoDiscoveryService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RepoDiscoveryService(
        IHttpClientFactory httpClientFactory,
        ICodeRepositoryRepository repoRepo,
        ILogger<RepoDiscoveryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repoRepo = repoRepo;
        _logger = logger;
    }

    public async Task<List<DiscoveredRepo>> DiscoverGitHubAsync(
        string organization, string? pat = null,
        bool excludeArchived = true, bool excludeForks = true,
        CancellationToken ct = default)
    {
        organization = ParseGitHubOrg(organization);

        var client = _httpClientFactory.CreateClient("Discovery");
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Andy.CodeIndex", "1.0"));
        if (pat is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);

        var repos = new List<DiscoveredRepo>();
        var page = 1;

        while (true)
        {
            var url = $"https://api.github.com/orgs/{organization}/repos?per_page=100&page={page}&type=all";
            var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {Status} for org {Org}", response.StatusCode, organization);
                break;
            }

            var items = await response.Content.ReadFromJsonAsync<List<GitHubRepo>>(JsonOptions, ct);
            if (items is null || items.Count == 0) break;

            foreach (var item in items)
            {
                if (excludeArchived && item.Archived) continue;
                if (excludeForks && item.Fork) continue;
                if (item.Disabled) continue;

                repos.Add(new DiscoveredRepo
                {
                    Name = item.Name,
                    FullName = item.FullName,
                    CloneUrl = item.CloneUrl,
                    Provider = "GitHub",
                    DefaultBranch = item.DefaultBranch,
                    Description = item.Description,
                    IsArchived = item.Archived,
                    IsFork = item.Fork,
                    IsDisabled = item.Disabled,
                    Stars = item.StargazersCount,
                    OpenIssues = item.OpenIssuesCount,
                    Language = item.Language,
                    LastPushedAt = item.PushedAt,
                    Size = item.Size
                });
            }

            if (items.Count < 100) break;
            page++;
        }

        await MarkTracked(repos, ct);
        _logger.LogInformation("Discovered {Count} GitHub repos in {Org}", repos.Count, organization);
        return repos;
    }

    public async Task<List<DiscoveredRepo>> DiscoverAzureDevOpsAsync(
        string organization, string? project = null, string? pat = null,
        CancellationToken ct = default)
    {
        organization = ParseAzureDevOpsOrg(organization);

        var client = _httpClientFactory.CreateClient("Discovery");
        if (pat is not null)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        var repos = new List<DiscoveredRepo>();

        // Get projects (or use the one specified)
        var projects = new List<string>();
        if (project is not null)
        {
            projects.Add(project);
        }
        else
        {
            var projectsUrl = $"https://dev.azure.com/{organization}/_apis/projects?api-version=7.0";
            var projectResponse = await client.GetFromJsonAsync<AdoResponse<AdoProject>>(projectsUrl, JsonOptions, ct);
            if (projectResponse?.Value is not null)
                projects.AddRange(projectResponse.Value.Select(p => p.Name));
        }

        // Get repos per project
        foreach (var proj in projects)
        {
            var reposUrl = $"https://dev.azure.com/{organization}/{proj}/_apis/git/repositories?api-version=7.0";
            try
            {
                var repoResponse = await client.GetFromJsonAsync<AdoResponse<AdoRepo>>(reposUrl, JsonOptions, ct);
                if (repoResponse?.Value is null) continue;

                foreach (var item in repoResponse.Value)
                {
                    if (item.IsDisabled) continue;

                    var branch = item.DefaultBranch;
                    if (branch?.StartsWith("refs/heads/") == true)
                        branch = branch["refs/heads/".Length..];

                    repos.Add(new DiscoveredRepo
                    {
                        Name = item.Name,
                        FullName = $"{organization}/{proj}/{item.Name}",
                        CloneUrl = item.RemoteUrl,
                        Provider = "AzureDevOps",
                        DefaultBranch = branch,
                        IsDisabled = item.IsDisabled
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list repos for {Org}/{Project}", organization, proj);
            }
        }

        await MarkTracked(repos, ct);
        _logger.LogInformation("Discovered {Count} Azure DevOps repos in {Org}", repos.Count, organization);
        return repos;
    }

    /// <summary>Extract org name from input that may be a full URL or just the org name.</summary>
    internal static string ParseGitHubOrg(string input)
    {
        input = input.Trim();
        // Handle: https://github.com/rivoli-ai or github.com/rivoli-ai
        if (input.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var uri = input.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(input) : new Uri("https://" + input);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            return segments[0];
        }
        return input;
    }

    /// <summary>Extract org name from input that may be a full URL or just the org name.</summary>
    internal static string ParseAzureDevOpsOrg(string input)
    {
        input = input.Trim();
        // Handle: https://dev.azure.com/myorg or dev.azure.com/myorg
        if (input.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var uri = input.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(input) : new Uri("https://" + input);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            return segments[0];
        }
        return input;
    }

    private async Task MarkTracked(List<DiscoveredRepo> repos, CancellationToken ct)
    {
        var tracked = await _repoRepo.GetAllAsync(ct);
        var trackedUrls = new HashSet<string>(
            tracked.Select(r => r.Url), StringComparer.OrdinalIgnoreCase);

        foreach (var repo in repos)
        {
            try
            {
                var normalized = RepositoryService.NormalizeUrl(repo.CloneUrl);
                repo.AlreadyTracked = trackedUrls.Contains(normalized);
            }
            catch
            {
                // If normalization fails, fall back to direct comparison
                repo.AlreadyTracked = trackedUrls.Contains(repo.CloneUrl);
            }
        }
    }

    // --- GitHub API DTOs ---
    private class GitHubRepo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
        [JsonPropertyName("clone_url")] public string CloneUrl { get; set; } = "";
        [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("archived")] public bool Archived { get; set; }
        [JsonPropertyName("fork")] public bool Fork { get; set; }
        [JsonPropertyName("disabled")] public bool Disabled { get; set; }
        [JsonPropertyName("stargazers_count")] public int? StargazersCount { get; set; }
        [JsonPropertyName("open_issues_count")] public int? OpenIssuesCount { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("pushed_at")] public DateTime? PushedAt { get; set; }
        [JsonPropertyName("size")] public int? Size { get; set; }
    }

    // --- Azure DevOps API DTOs ---
    private class AdoResponse<T> { public List<T> Value { get; set; } = []; }
    private class AdoProject { public string Name { get; set; } = ""; }
    private class AdoRepo
    {
        public string Name { get; set; } = "";
        public string RemoteUrl { get; set; } = "";
        public string? DefaultBranch { get; set; }
        public bool IsDisabled { get; set; }
    }
}
