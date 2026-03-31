using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly CodeIndexDbContext _context;
    private readonly IApiKeyResolver _apiKeyResolver;
    private readonly EnrichmentLlmOptions _llmOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ReportService> _logger;

    private static readonly Dictionary<EnrichmentSubtype, string> InsightLayerNames = new()
    {
        [EnrichmentSubtype.FeatureMap] = "Feature Map",
        [EnrichmentSubtype.ArchitectureAnalysis] = "Architecture Analysis",
        [EnrichmentSubtype.DesignAnalysis] = "Design Analysis",
        [EnrichmentSubtype.ImplementationAnalysis] = "Implementation Analysis",
        [EnrichmentSubtype.DependencyAnalysis] = "Dependency Analysis",
        [EnrichmentSubtype.TestAnalysis] = "Test Analysis",
        [EnrichmentSubtype.SecurityAnalysis] = "Security Analysis",
        [EnrichmentSubtype.DeploymentAnalysis] = "Deployment Analysis",
        [EnrichmentSubtype.OperationsAnalysis] = "Operations Analysis",
        [EnrichmentSubtype.LocalSetupGuide] = "Local Setup Guide",
        [EnrichmentSubtype.TechStack] = "Technology Stack",
    };

    public ReportService(
        CodeIndexDbContext context,
        IApiKeyResolver apiKeyResolver,
        IOptions<EnrichmentLlmOptions> llmOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<ReportService> logger)
    {
        _context = context;
        _apiKeyResolver = apiKeyResolver;
        _llmOptions = llmOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ReportDto> GenerateReportAsync(Guid repositoryId, CancellationToken ct = default, bool regenerate = false)
    {
        var repo = await _context.Repositories.FindAsync([repositoryId], ct)
            ?? throw new KeyNotFoundException($"Repository {repositoryId} not found");

        // Check for cached report (skip if regenerating)
        if (!regenerate)
        {
            var cachedReport = await _context.Enrichments
                .Where(e => e.RepositoryId == repositoryId && e.Subtype == EnrichmentSubtype.InsightReport)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (cachedReport is not null)
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<ReportDto>(cachedReport.Content, JsonOptions);
                    if (cached is not null)
                        return cached;
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Cached report for {Repo} had invalid JSON, regenerating", repo.Name);
                }
            }
        }

        // Load all insight enrichments for this repo
        var insightSubtypes = InsightLayerNames.Keys.ToArray();
        var insights = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId
                && e.Type == EnrichmentType.Insights
                && insightSubtypes.Contains(e.Subtype))
            .ToListAsync(ct);

        if (insights.Count == 0)
            throw new InvalidOperationException("No insights found. Generate insights first using the insights endpoint.");

        // Build the LLM prompt with all insight contents
        var (apiKey, baseUrl, model, source) = await _apiKeyResolver.ResolveLlmKeyAsync("anonymous", ct);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("No LLM API key configured. Cannot generate report.");

        var report = new ReportDto
        {
            RepositoryName = repo.Name,
            GeneratedAt = DateTime.UtcNow
        };

        // Call LLM once to rate all layers
        var llmAnalysis = await CallLlmForAnalysisAsync(apiKey, model, repo.Name, insights, ct, baseUrl);

        if (llmAnalysis is null)
            throw new InvalidOperationException(
                "Report generation failed: the LLM did not return a valid analysis. " +
                "This may be due to an API error, context size limits, or an unsupported model parameter. " +
                "Check the server logs for details and try again.");

        // Build layer reports from insights + LLM analysis
        foreach (var insight in insights)
        {
            if (!InsightLayerNames.TryGetValue(insight.Subtype, out var layerName))
                continue;

            var layerAnalysis = llmAnalysis?.Layers
                ?.FirstOrDefault(l => l.Subtype?.Equals(insight.Subtype.ToString(), StringComparison.OrdinalIgnoreCase) == true);

            report.Layers.Add(new LayerReportDto
            {
                Name = layerName,
                Subtype = insight.Subtype.ToString(),
                MaturityRating = Clamp(layerAnalysis?.MaturityRating ?? 3, 1, 5),
                QualityRating = Clamp(layerAnalysis?.QualityRating ?? 3, 1, 5),
                RiskRating = Clamp(layerAnalysis?.RiskRating ?? 3, 1, 5),
                Strengths = layerAnalysis?.Strengths ?? [],
                Weaknesses = layerAnalysis?.Weaknesses ?? [],
                Recommendations = layerAnalysis?.Recommendations ?? [],
                Content = insight.Content,
                HasMermaidDiagrams = insight.Content.Contains("```mermaid", StringComparison.OrdinalIgnoreCase)
            });
        }

        // Set top 5 improvements from LLM
        report.Top5Improvements = llmAnalysis?.Improvements?.Take(5).Select(i => new ImprovementDto
        {
            Title = i.Title ?? "Improvement",
            Description = i.Description ?? "",
            Layer = i.Layer ?? "",
            Impact = NormalizeImpactEffort(i.Impact, "medium"),
            Effort = NormalizeImpactEffort(i.Effort, "medium")
        }).ToList() ?? [];

        // Calculate overall health score
        report.OverallHealthScore = llmAnalysis?.OverallHealthScore is > 0 and <= 100
            ? llmAnalysis.OverallHealthScore
            : CalculateHealthScore(report.Layers);

        // Calculate velocity from commits table
        report.Velocity = await CalculateVelocityAsync(repositoryId, ct);

        // Populate TechStack from file extensions and TechStack enrichment
        report.TechStack = await BuildTechStackAsync(repositoryId, ct);

        // Cache the report as an InsightReport enrichment
        await CacheReportAsync(repo, report, ct);

        return report;
    }

    public async Task<string> ExportHtmlAsync(Guid repositoryId, CancellationToken ct = default)
    {
        var report = await GenerateReportAsync(repositoryId, ct);
        return BuildHtml(report);
    }

    internal async Task<LlmAnalysisResponse?> CallLlmForAnalysisAsync(
        string apiKey, string model, string repoName, List<Enrichment> insights, CancellationToken ct, string? overrideBaseUrl = null)
    {
        var sb = new StringBuilder();
        foreach (var insight in insights)
        {
            if (!InsightLayerNames.TryGetValue(insight.Subtype, out var name))
                continue;

            var content = insight.Content.Length > 2500
                ? insight.Content[..2500] + "\n... (truncated)"
                : insight.Content;

            sb.AppendLine($"=== {name} ({insight.Subtype}) ===");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        var prompt = "You are analyzing insight layers for the repository \"" + repoName + "\".\n" +
            "Rate each layer and provide constructive feedback.\n\n" +
            "IMPORTANT: Return ONLY a valid JSON object. No preamble, no explanation, no markdown fencing, no text before or after the JSON.\n\n" +
            "JSON structure:\n" +
            "- overallHealthScore: number 0-100\n" +
            "- layers: array of objects, each with:\n" +
            "  - subtype: string (e.g., \"FeatureMap\")\n" +
            "  - maturityRating: number 1-5\n" +
            "  - qualityRating: number 1-5\n" +
            "  - riskRating: number 1-5\n" +
            "  - strengths: array of 3 strings (specific, referencing actual code/patterns found)\n" +
            "  - weaknesses: array of 3 strings (specific, with concrete examples)\n" +
            "  - recommendations: array of 3 strings (actionable, prioritized)\n" +
            "- improvements: array of 5 objects with title, description, layer, impact (high/medium/low), effort (high/medium/low)\n\n" +
            "Base your analysis on the actual content below — be specific, not generic.\n\n" +
            "Insight contents:\n" + sb.ToString();

        var client = _httpClientFactory.CreateClient("Chat");
        var baseUrl = (overrideBaseUrl ?? _llmOptions.BaseUrl).TrimEnd('/') + "/";

        var isReasoningModel = model.StartsWith("gpt-5") || model.StartsWith("o1") || model.StartsWith("o3");
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content = prompt } }
        };
        if (!isReasoningModel) body["temperature"] = 0.3;
        body[isReasoningModel ? "max_completion_tokens" : "max_tokens"] = 4000;

        try
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl + "chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = JsonContent.Create(body);
            var response = await client.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("LLM API returned {Status}: {Error}", response.StatusCode, errorBody);
                return null;
            }
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var reply = result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            if (string.IsNullOrEmpty(reply))
                return null;

            return ParseLlmResponse(reply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for report generation");
            return null;
        }
    }

    internal static LlmAnalysisResponse? ParseLlmResponse(string reply)
    {
        // Strip markdown code fences if present
        var json = reply.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline > 0)
                json = json[(firstNewline + 1)..];
            if (json.EndsWith("```"))
                json = json[..^3];
            json = json.Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<LlmAnalysisResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Try to extract JSON from the response
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    return JsonSerializer.Deserialize<LlmAnalysisResponse>(json[start..(end + 1)], JsonOptions);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }
    }

    internal async Task<VelocityDto> CalculateVelocityAsync(Guid repositoryId, CancellationToken ct)
    {
        var allCommits = await _context.Commits
            .Where(c => c.RepositoryId == repositoryId)
            .ToListAsync(ct);

        if (allCommits.Count == 0)
            return new VelocityDto { CommitsPerMonth = 0, ActiveContributors = 0, Trend = "none" };

        var earliest = allCommits.Min(c => c.CommittedAt);
        var latest = allCommits.Max(c => c.CommittedAt);
        var totalMonths = Math.Max(1, (latest - earliest).TotalDays / 30.0);
        var commitsPerMonth = allCommits.Count / totalMonths;

        var activeContributors = allCommits
            .Where(c => !string.IsNullOrEmpty(c.AuthorEmail))
            .Select(c => c.AuthorEmail)
            .Distinct()
            .Count();

        // Trend: compare last half vs first half of commits
        var midpoint = earliest + (latest - earliest) / 2;
        var firstHalf = allCommits.Count(c => c.CommittedAt < midpoint);
        var secondHalf = allCommits.Count(c => c.CommittedAt >= midpoint);

        string trend;
        if (secondHalf > firstHalf * 1.2)
            trend = "increasing";
        else if (secondHalf < firstHalf * 0.8)
            trend = "decreasing";
        else
            trend = "stable";

        var topContributors = allCommits
            .GroupBy(c => new { c.AuthorName, c.AuthorEmail })
            .Select(g => new ContributorDto
            {
                Name = g.Key.AuthorName ?? g.Key.AuthorEmail ?? "unknown",
                Email = g.Key.AuthorEmail ?? "",
                Commits = g.Count()
            })
            .OrderByDescending(c => c.Commits)
            .Take(10)
            .ToList();

        return new VelocityDto
        {
            CommitsPerMonth = Math.Round(commitsPerMonth, 1),
            ActiveContributors = activeContributors,
            Trend = trend,
            TopContributors = topContributors
        };
    }

    internal async Task<TechStackDto> BuildTechStackAsync(Guid repositoryId, CancellationToken ct)
    {
        var dto = new TechStackDto();

        // Language breakdown from RepositoryFiles
        var latestCommit = await _context.Commits
            .Where(c => c.RepositoryId == repositoryId)
            .OrderByDescending(c => c.CommittedAt)
            .FirstOrDefaultAsync(ct);

        if (latestCommit is not null)
        {
            var files = await _context.RepositoryFiles
                .Where(f => f.CommitId == latestCommit.Id)
                .ToListAsync(ct);

            var total = files.Count;
            if (total > 0)
            {
                dto.Languages = files
                    .Where(f => !string.IsNullOrEmpty(f.Language))
                    .GroupBy(f => f.Language!)
                    .Select(g => new LanguageBreakdown
                    {
                        Name = g.Key,
                        FileCount = g.Count(),
                        Percentage = Math.Round(100.0 * g.Count() / total, 1)
                    })
                    .OrderByDescending(l => l.FileCount)
                    .ToList();
            }
        }

        // Parse TechStack enrichment content for backend/frontend/database/infrastructure
        var techStackEnrichment = await _context.Enrichments
            .Where(e => e.RepositoryId == repositoryId && e.Subtype == EnrichmentSubtype.TechStack)
            .FirstOrDefaultAsync(ct);

        if (techStackEnrichment is not null)
        {
            var content = techStackEnrichment.Content;
            dto.Backend = ExtractTechComponents(content, "## Backend");
            dto.Frontend = ExtractTechComponents(content, "## Frontend");
            dto.Database = ExtractTechComponents(content, "## Database");
            dto.Infrastructure = ExtractTechComponents(content, "## Infrastructure");
        }

        return dto;
    }

    internal static List<TechComponent> ExtractTechComponents(string content, string sectionHeader)
    {
        var components = new List<TechComponent>();

        var sectionIdx = content.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase);
        if (sectionIdx < 0) return components;

        var sectionStart = sectionIdx + sectionHeader.Length;
        var nextSection = content.IndexOf("\n## ", sectionStart, StringComparison.OrdinalIgnoreCase);
        var sectionText = nextSection > 0
            ? content[sectionStart..nextSection]
            : content[sectionStart..];

        // Look for known technology keywords with optional versions
        var techPatterns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".NET"] = [".NET", "ASP.NET", "dotnet"],
            ["Angular"] = ["Angular"],
            ["React"] = ["React"],
            ["Vue"] = ["Vue.js", "Vue"],
            ["Node.js"] = ["Node.js", "Node"],
            ["Go"] = ["Go ", "Golang"],
            ["Rust"] = ["Rust"],
            ["Python"] = ["Python", "Django", "Flask", "FastAPI"],
            ["Java"] = ["Java ", "Spring"],
            ["PostgreSQL"] = ["PostgreSQL", "Postgres"],
            ["SQL Server"] = ["SQL Server", "MSSQL"],
            ["MySQL"] = ["MySQL"],
            ["MongoDB"] = ["MongoDB", "Mongo"],
            ["Redis"] = ["Redis"],
            ["Docker"] = ["Docker"],
            ["Kubernetes"] = ["Kubernetes", "K8s"],
            ["GitHub Actions"] = ["GitHub Actions"],
            ["Azure DevOps"] = ["Azure DevOps", "Azure Pipelines"],
            ["Jenkins"] = ["Jenkins"],
            ["GitLab CI"] = ["GitLab CI"],
            ["Terraform"] = ["Terraform"],
        };

        foreach (var (name, keywords) in techPatterns)
        {
            foreach (var keyword in keywords)
            {
                if (sectionText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    // Try to extract a version number after the keyword
                    var version = ExtractVersion(sectionText, keyword);
                    components.Add(new TechComponent { Name = name, Version = version });
                    break; // One match per tech is enough
                }
            }
        }

        return components;
    }

    internal static string? ExtractVersion(string text, string keyword)
    {
        var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        // Look for a version pattern after the keyword: e.g., "Angular 17.2", ".NET 8", "v3.1.0"
        var after = text[(idx + keyword.Length)..];
        var match = System.Text.RegularExpressions.Regex.Match(after, @"^\s*v?(\d+(?:\.\d+){0,3})");
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static int CalculateHealthScore(List<LayerReportDto> layers)
    {
        if (layers.Count == 0) return 50;

        // Weighted average: maturity 40%, quality 40%, inverse-risk 20%
        var totalScore = 0.0;
        foreach (var layer in layers)
        {
            var maturityNorm = (layer.MaturityRating - 1) / 4.0 * 100;
            var qualityNorm = (layer.QualityRating - 1) / 4.0 * 100;
            var riskNorm = (5 - layer.RiskRating) / 4.0 * 100; // invert risk: low risk = high score
            totalScore += maturityNorm * 0.4 + qualityNorm * 0.4 + riskNorm * 0.2;
        }

        return Clamp((int)Math.Round(totalScore / layers.Count), 0, 100);
    }

    private async Task CacheReportAsync(Repository repo, ReportDto report, CancellationToken ct)
    {
        // Delete existing cached reports
        var existing = await _context.Enrichments
            .Where(e => e.RepositoryId == repo.Id && e.Subtype == EnrichmentSubtype.InsightReport)
            .ToListAsync(ct);
        _context.Enrichments.RemoveRange(existing);

        // Resolve commit ID
        Guid? commitId = null;
        if (repo.LastIndexedCommitSha is not null)
        {
            var commitRecord = await _context.Commits
                .FirstOrDefaultAsync(c => c.RepositoryId == repo.Id && c.Sha == repo.LastIndexedCommitSha, ct);
            commitId = commitRecord?.Id;
        }

        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            CommitId = commitId,
            Type = EnrichmentType.Insights,
            Subtype = EnrichmentSubtype.InsightReport,
            Title = $"Insight Report for {repo.Name}",
            Content = JsonSerializer.Serialize(report, JsonOptions),
            Quality = 1.0,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
    }

    internal static string BuildHtml(ReportDto report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>Insight Report - {Escape(report.RepositoryName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(GetCss());
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine($"<h1>Insight Report: {Escape(report.RepositoryName)}</h1>");
        sb.AppendLine($"<p class=\"generated-at\">Generated {report.GeneratedAt:yyyy-MM-dd HH:mm} UTC</p>");
        sb.AppendLine("</div>");

        // Health Score
        var scoreColor = report.OverallHealthScore > 70 ? "#22c55e"
            : report.OverallHealthScore >= 40 ? "#eab308"
            : "#ef4444";
        sb.AppendLine("<div class=\"score-section\">");
        sb.AppendLine($"<div class=\"score-number\" style=\"color:{scoreColor}\">{report.OverallHealthScore}</div>");
        sb.AppendLine("<div class=\"score-label\">Overall Health Score</div>");
        sb.AppendLine("</div>");

        // Velocity
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("<h2>Velocity</h2>");
        sb.AppendLine("<div class=\"metrics\">");
        sb.AppendLine($"<div class=\"metric\"><span class=\"metric-value\">{report.Velocity.CommitsPerMonth:F1}</span><span class=\"metric-label\">Commits/Month</span></div>");
        sb.AppendLine($"<div class=\"metric\"><span class=\"metric-value\">{report.Velocity.ActiveContributors}</span><span class=\"metric-label\">Active Contributors</span></div>");
        sb.AppendLine($"<div class=\"metric\"><span class=\"metric-value\">{Escape(report.Velocity.Trend)}</span><span class=\"metric-label\">Trend</span></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // Top 5 Improvements
        if (report.Top5Improvements.Count > 0)
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("<h2>Top Improvements</h2>");
            sb.AppendLine("<table class=\"improvements-table\">");
            sb.AppendLine("<thead><tr><th>#</th><th>Title</th><th>Layer</th><th>Impact</th><th>Effort</th><th>Description</th></tr></thead>");
            sb.AppendLine("<tbody>");
            for (var i = 0; i < report.Top5Improvements.Count; i++)
            {
                var imp = report.Top5Improvements[i];
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{Escape(imp.Title)}</td><td>{Escape(imp.Layer)}</td><td><span class=\"badge badge-{imp.Impact}\">{Escape(imp.Impact)}</span></td><td><span class=\"badge badge-{imp.Effort}\">{Escape(imp.Effort)}</span></td><td>{Escape(imp.Description)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
        }

        // Layer reports
        foreach (var layer in report.Layers)
        {
            sb.AppendLine("<div class=\"section layer-section\">");
            sb.AppendLine($"<h2>{Escape(layer.Name)}</h2>");
            sb.AppendLine("<div class=\"ratings\">");
            sb.AppendLine($"<div class=\"rating\"><span class=\"rating-label\">Maturity</span>{RenderStars(layer.MaturityRating)}</div>");
            sb.AppendLine($"<div class=\"rating\"><span class=\"rating-label\">Quality</span>{RenderStars(layer.QualityRating)}</div>");
            sb.AppendLine($"<div class=\"rating\"><span class=\"rating-label\">Risk</span>{RenderStars(layer.RiskRating)}</div>");
            sb.AppendLine("</div>");

            if (layer.Strengths.Count > 0)
            {
                sb.AppendLine("<h3>Strengths</h3><ul>");
                foreach (var s in layer.Strengths) sb.AppendLine($"<li>{Escape(s)}</li>");
                sb.AppendLine("</ul>");
            }

            if (layer.Weaknesses.Count > 0)
            {
                sb.AppendLine("<h3>Weaknesses</h3><ul>");
                foreach (var w in layer.Weaknesses) sb.AppendLine($"<li>{Escape(w)}</li>");
                sb.AppendLine("</ul>");
            }

            if (layer.Recommendations.Count > 0)
            {
                sb.AppendLine("<h3>Recommendations</h3><ul>");
                foreach (var r in layer.Recommendations) sb.AppendLine($"<li>{Escape(r)}</li>");
                sb.AppendLine("</ul>");
            }

            // Full content rendered as HTML (basic markdown to HTML conversion)
            sb.AppendLine("<div class=\"content\">");
            sb.AppendLine(MarkdownToHtml(layer.Content));
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }

        sb.AppendLine("<div class=\"footer\">Generated by Andy Code Index</div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string RenderStars(int rating)
    {
        var filled = Math.Clamp(rating, 0, 5);
        var empty = 5 - filled;
        return "<span class=\"stars\">"
            + string.Concat(Enumerable.Repeat("<span class=\"star filled\">&#9733;</span>", filled))
            + string.Concat(Enumerable.Repeat("<span class=\"star empty\">&#9734;</span>", empty))
            + "</span>";
    }

    private static string Escape(string? text) =>
        System.Net.WebUtility.HtmlEncode(text ?? "");

    private static int Clamp(int value, int min, int max) =>
        Math.Max(min, Math.Min(max, value));

    /// <summary>Basic markdown to HTML conversion for the export.</summary>
    private static string MarkdownToHtml(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var lines = markdown.Split('\n');
        var sb = new StringBuilder();
        var inCodeBlock = false;
        var inList = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Code blocks
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    sb.AppendLine("</code></pre>");
                    inCodeBlock = false;
                }
                else
                {
                    var lang = line.TrimStart().Length > 3 ? line.TrimStart()[3..].Trim() : "";
                    sb.AppendLine($"<pre><code class=\"language-{Escape(lang)}\">");
                    inCodeBlock = true;
                }
                continue;
            }
            if (inCodeBlock) { sb.AppendLine(Escape(line)); continue; }

            // Close list if needed
            if (inList && !line.TrimStart().StartsWith("- ") && !line.TrimStart().StartsWith("* "))
            {
                sb.AppendLine("</ul>"); inList = false;
            }

            // Headings
            if (line.StartsWith("### ")) { sb.AppendLine($"<h3>{Escape(line[4..])}</h3>"); continue; }
            if (line.StartsWith("## ")) { sb.AppendLine($"<h2>{Escape(line[3..])}</h2>"); continue; }
            if (line.StartsWith("# ")) { sb.AppendLine($"<h1>{Escape(line[2..])}</h1>"); continue; }

            // List items
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                var content = line.TrimStart()[2..];
                // Bold
                content = System.Text.RegularExpressions.Regex.Replace(Escape(content), @"\*\*(.+?)\*\*", "<strong>$1</strong>");
                sb.AppendLine($"<li>{content}</li>");
                continue;
            }

            // Empty line
            if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine("<br/>"); continue; }

            // Regular paragraph with inline formatting
            var text = Escape(line);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
            sb.AppendLine($"<p>{text}</p>");
        }

        if (inList) sb.AppendLine("</ul>");
        if (inCodeBlock) sb.AppendLine("</code></pre>");
        return sb.ToString();
    }

    private static string NormalizeImpactEffort(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        var lower = value.ToLowerInvariant().Trim();
        return lower is "high" or "medium" or "low" ? lower : fallback;
    }

    private static string GetCss() => """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #1a1a2e; background: #f8f9fa; padding: 2rem; max-width: 1000px; margin: 0 auto; line-height: 1.6; }
        .header { text-align: center; margin-bottom: 2rem; }
        .header h1 { font-size: 1.75rem; color: #1a1a2e; }
        .generated-at { color: #6b7280; font-size: 0.875rem; }
        .score-section { text-align: center; margin: 2rem 0; padding: 2rem; background: white; border-radius: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
        .score-number { font-size: 4rem; font-weight: 700; line-height: 1; }
        .score-label { font-size: 1rem; color: #6b7280; margin-top: 0.5rem; }
        .section { background: white; border-radius: 12px; padding: 1.5rem; margin-bottom: 1.5rem; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
        .section h2 { font-size: 1.25rem; margin-bottom: 1rem; color: #1a1a2e; border-bottom: 2px solid #e5e7eb; padding-bottom: 0.5rem; }
        .section h3 { font-size: 1rem; margin: 1rem 0 0.5rem 0; color: #374151; }
        .section ul { margin-left: 1.5rem; margin-bottom: 0.5rem; }
        .section li { margin-bottom: 0.25rem; }
        .metrics { display: flex; gap: 2rem; flex-wrap: wrap; }
        .metric { text-align: center; flex: 1; min-width: 120px; }
        .metric-value { display: block; font-size: 1.5rem; font-weight: 700; color: #2563eb; }
        .metric-label { display: block; font-size: 0.75rem; color: #6b7280; text-transform: uppercase; letter-spacing: 0.05em; }
        .ratings { display: flex; gap: 2rem; flex-wrap: wrap; margin-bottom: 1rem; }
        .rating { display: flex; align-items: center; gap: 0.5rem; }
        .rating-label { font-size: 0.875rem; color: #374151; font-weight: 500; min-width: 70px; }
        .stars { display: inline-flex; gap: 2px; }
        .star { font-size: 1.25rem; }
        .star.filled { color: #f59e0b; }
        .star.empty { color: #d1d5db; }
        .improvements-table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
        .improvements-table th { text-align: left; padding: 0.5rem; border-bottom: 2px solid #e5e7eb; color: #6b7280; font-weight: 600; }
        .improvements-table td { padding: 0.5rem; border-bottom: 1px solid #f3f4f6; }
        .badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 9999px; font-size: 0.75rem; font-weight: 500; text-transform: capitalize; }
        .badge-high { background: #fef2f2; color: #dc2626; }
        .badge-medium { background: #fffbeb; color: #d97706; }
        .badge-low { background: #f0fdf4; color: #16a34a; }
        details { margin-top: 1rem; }
        summary { cursor: pointer; color: #2563eb; font-size: 0.875rem; }
        .content pre { white-space: pre-wrap; word-wrap: break-word; font-size: 0.8rem; background: #f9fafb; padding: 1rem; border-radius: 8px; margin-top: 0.5rem; overflow-x: auto; }
        .footer { text-align: center; color: #9ca3af; font-size: 0.75rem; margin-top: 2rem; padding-top: 1rem; border-top: 1px solid #e5e7eb; }
        @media print {
            body { padding: 0; background: white; }
            .section { box-shadow: none; border: 1px solid #e5e7eb; break-inside: avoid; }
            .score-section { box-shadow: none; border: 1px solid #e5e7eb; }
            details { open: true; }
            details > summary { display: none; }
            details > .content { display: block; }
        }
        """;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // LLM response models for deserialization
    internal class LlmAnalysisResponse
    {
        [JsonPropertyName("overallHealthScore")]
        public int OverallHealthScore { get; set; }

        [JsonPropertyName("layers")]
        public List<LlmLayerAnalysis>? Layers { get; set; }

        [JsonPropertyName("improvements")]
        public List<LlmImprovement>? Improvements { get; set; }
    }

    internal class LlmLayerAnalysis
    {
        [JsonPropertyName("subtype")]
        public string? Subtype { get; set; }

        [JsonPropertyName("maturityRating")]
        public int MaturityRating { get; set; }

        [JsonPropertyName("qualityRating")]
        public int QualityRating { get; set; }

        [JsonPropertyName("riskRating")]
        public int RiskRating { get; set; }

        [JsonPropertyName("strengths")]
        public List<string>? Strengths { get; set; }

        [JsonPropertyName("weaknesses")]
        public List<string>? Weaknesses { get; set; }

        [JsonPropertyName("recommendations")]
        public List<string>? Recommendations { get; set; }
    }

    internal class LlmImprovement
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("layer")]
        public string? Layer { get; set; }

        [JsonPropertyName("impact")]
        public string? Impact { get; set; }

        [JsonPropertyName("effort")]
        public string? Effort { get; set; }
    }
}
