using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class ReportServiceTests
{
    private readonly CodeIndexDbContext _context;
    private readonly Mock<IApiKeyResolver> _apiKeyResolverMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<ILogger<ReportService>> _loggerMock = new();
    private readonly IOptions<EnrichmentLlmOptions> _llmOptions;

    public ReportServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _llmOptions = Options.Create(new EnrichmentLlmOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini"
        });
    }

    [Fact]
    public async Task GenerateReportAsync_NoInsights_ThrowsInvalidOperation()
    {
        // Arrange
        var repo = new Repository { Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/test/repo", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _context.Repositories.Add(repo);
        await _context.SaveChangesAsync();

        var service = CreateService();

        // Act & Assert
        var act = () => service.GenerateReportAsync(repo.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Generate insights first*");
    }

    [Fact]
    public async Task GenerateReportAsync_RepositoryNotFound_ThrowsKeyNotFound()
    {
        var service = CreateService();

        var act = () => service.GenerateReportAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GenerateReportAsync_CachedReport_ReturnsCachedWithoutLlmCall()
    {
        // Arrange
        var repo = new Repository { Id = Guid.NewGuid(), Name = "cached-repo", Url = "https://github.com/test/cached", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _context.Repositories.Add(repo);

        var cachedReport = new ReportDto
        {
            RepositoryName = "cached-repo",
            GeneratedAt = DateTime.UtcNow,
            OverallHealthScore = 75,
            Velocity = new VelocityDto { CommitsPerMonth = 10, ActiveContributors = 3, Trend = "stable" },
            Layers = [new LayerReportDto { Name = "Feature Map", Subtype = "FeatureMap", MaturityRating = 4, QualityRating = 4, RiskRating = 2 }],
            Top5Improvements = [new ImprovementDto { Title = "Improve testing", Layer = "TestAnalysis", Impact = "high", Effort = "medium" }]
        };

        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Type = EnrichmentType.Insights,
            Subtype = EnrichmentSubtype.InsightReport,
            Title = "Insight Report",
            Content = System.Text.Json.JsonSerializer.Serialize(cachedReport, ReportService.JsonOptions),
            Quality = 1.0,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var service = CreateService();

        // Act
        var result = await service.GenerateReportAsync(repo.Id);

        // Assert
        result.Should().NotBeNull();
        result.RepositoryName.Should().Be("cached-repo");
        result.OverallHealthScore.Should().Be(75);
        result.Layers.Should().HaveCount(1);
        result.Top5Improvements.Should().HaveCount(1);

        // Verify LLM was NOT called
        _apiKeyResolverMock.Verify(
            r => r.ResolveLlmKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void CalculateHealthScore_AllPerfectRatings_Returns100()
    {
        var layers = new List<LayerReportDto>
        {
            new() { MaturityRating = 5, QualityRating = 5, RiskRating = 1 },
            new() { MaturityRating = 5, QualityRating = 5, RiskRating = 1 },
        };

        var score = ReportService.CalculateHealthScore(layers);
        score.Should().Be(100);
    }

    [Fact]
    public void CalculateHealthScore_AllWorstRatings_Returns0()
    {
        var layers = new List<LayerReportDto>
        {
            new() { MaturityRating = 1, QualityRating = 1, RiskRating = 5 },
            new() { MaturityRating = 1, QualityRating = 1, RiskRating = 5 },
        };

        var score = ReportService.CalculateHealthScore(layers);
        score.Should().Be(0);
    }

    [Fact]
    public void CalculateHealthScore_EmptyLayers_Returns50()
    {
        var score = ReportService.CalculateHealthScore([]);
        score.Should().Be(50);
    }

    [Fact]
    public void CalculateHealthScore_MixedRatings_ReturnsWeightedAverage()
    {
        var layers = new List<LayerReportDto>
        {
            new() { MaturityRating = 3, QualityRating = 4, RiskRating = 2 },
        };

        // maturityNorm = (3-1)/4*100 = 50
        // qualityNorm = (4-1)/4*100 = 75
        // riskNorm = (5-2)/4*100 = 75
        // total = 50*0.4 + 75*0.4 + 75*0.2 = 20 + 30 + 15 = 65
        var score = ReportService.CalculateHealthScore(layers);
        score.Should().Be(65);
    }

    [Fact]
    public void ParseLlmResponse_ValidJson_ParsesCorrectly()
    {
        var json = """
        {
            "overallHealthScore": 72,
            "layers": [
                {
                    "subtype": "FeatureMap",
                    "maturityRating": 4,
                    "qualityRating": 3,
                    "riskRating": 2,
                    "strengths": ["Good coverage", "Well organized"],
                    "weaknesses": ["Missing docs"],
                    "recommendations": ["Add more tests"]
                }
            ],
            "improvements": [
                {
                    "title": "Improve testing",
                    "description": "Add integration tests",
                    "layer": "TestAnalysis",
                    "impact": "high",
                    "effort": "medium"
                }
            ]
        }
        """;

        var result = ReportService.ParseLlmResponse(json);

        result.Should().NotBeNull();
        result!.OverallHealthScore.Should().Be(72);
        result.Layers.Should().HaveCount(1);
        result.Layers![0].Subtype.Should().Be("FeatureMap");
        result.Layers[0].MaturityRating.Should().Be(4);
        result.Layers[0].Strengths.Should().Contain("Good coverage");
        result.Improvements.Should().HaveCount(1);
        result.Improvements![0].Title.Should().Be("Improve testing");
    }

    [Fact]
    public void ParseLlmResponse_WrappedInCodeFence_ParsesCorrectly()
    {
        var json = """
        ```json
        {
            "overallHealthScore": 60,
            "layers": [],
            "improvements": []
        }
        ```
        """;

        var result = ReportService.ParseLlmResponse(json);

        result.Should().NotBeNull();
        result!.OverallHealthScore.Should().Be(60);
    }

    [Fact]
    public void ParseLlmResponse_InvalidJson_ReturnsNull()
    {
        var result = ReportService.ParseLlmResponse("This is not JSON at all.");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseLlmResponse_JsonWithPreamble_ExtractsAndParses()
    {
        var json = """
        Here is the analysis:
        {"overallHealthScore": 55, "layers": [], "improvements": []}
        Some trailing text.
        """;

        var result = ReportService.ParseLlmResponse(json);
        result.Should().NotBeNull();
        result!.OverallHealthScore.Should().Be(55);
    }

    [Fact]
    public void BuildHtml_ProducesValidHtmlWithAllSections()
    {
        var report = new ReportDto
        {
            RepositoryName = "test-repo",
            GeneratedAt = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            OverallHealthScore = 78,
            Velocity = new VelocityDto { CommitsPerMonth = 15.3, ActiveContributors = 4, Trend = "increasing" },
            Layers =
            [
                new LayerReportDto
                {
                    Name = "Architecture Analysis",
                    Subtype = "ArchitectureAnalysis",
                    MaturityRating = 4,
                    QualityRating = 3,
                    RiskRating = 2,
                    Strengths = ["Clean separation of concerns", "Good API design"],
                    Weaknesses = ["Missing monitoring"],
                    Recommendations = ["Add health checks", "Implement circuit breakers"],
                    Content = "Architecture analysis content with ```mermaid\ngraph TD\nA-->B\n```",
                    HasMermaidDiagrams = true
                }
            ],
            Top5Improvements =
            [
                new ImprovementDto { Title = "Add monitoring", Description = "Implement centralized logging", Layer = "Operations", Impact = "high", Effort = "medium" },
                new ImprovementDto { Title = "Increase test coverage", Description = "Add integration tests", Layer = "Testing", Impact = "high", Effort = "high" }
            ]
        };

        var html = ReportService.BuildHtml(report);

        // Validate HTML structure
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("<html");
        html.Should().Contain("</html>");
        html.Should().Contain("<head>");
        html.Should().Contain("<body>");

        // Health score section
        html.Should().Contain("78");
        html.Should().Contain("Overall Health Score");
        html.Should().Contain("#22c55e"); // green for score > 70

        // Velocity section
        html.Should().Contain("15.3");
        html.Should().Contain("Commits/Month");
        html.Should().Contain("4");
        html.Should().Contain("Active Contributors");
        html.Should().Contain("increasing");

        // Layer section
        html.Should().Contain("Architecture Analysis");
        html.Should().Contain("&#9733;"); // filled star
        html.Should().Contain("&#9734;"); // empty star
        html.Should().Contain("Clean separation of concerns");
        html.Should().Contain("Missing monitoring");
        html.Should().Contain("Add health checks");

        // Improvements table
        html.Should().Contain("Add monitoring");
        html.Should().Contain("Increase test coverage");
        html.Should().Contain("badge-high");

        // Print CSS
        html.Should().Contain("@media print");

        // Repository name in title
        html.Should().Contain("test-repo");
    }

    [Fact]
    public void BuildHtml_LowScore_ShowsRedColor()
    {
        var report = new ReportDto
        {
            RepositoryName = "low-score-repo",
            GeneratedAt = DateTime.UtcNow,
            OverallHealthScore = 25,
            Velocity = new VelocityDto()
        };

        var html = ReportService.BuildHtml(report);
        html.Should().Contain("#ef4444"); // red for score < 40
    }

    [Fact]
    public void BuildHtml_MediumScore_ShowsYellowColor()
    {
        var report = new ReportDto
        {
            RepositoryName = "mid-score-repo",
            GeneratedAt = DateTime.UtcNow,
            OverallHealthScore = 55,
            Velocity = new VelocityDto()
        };

        var html = ReportService.BuildHtml(report);
        html.Should().Contain("#eab308"); // yellow for score 40-70
    }

    [Fact]
    public async Task CalculateVelocityAsync_WithCommits_ReturnsCorrectMetrics()
    {
        var repo = new Repository { Id = Guid.NewGuid(), Name = "velocity-repo", Url = "https://github.com/test/vel", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _context.Repositories.Add(repo);

        // Add 6 recent commits (within last 3 months) from 2 authors
        for (int i = 0; i < 6; i++)
        {
            _context.Commits.Add(new Commit
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Sha = $"abc{i:D4}",
                Message = $"commit {i}",
                AuthorName = i < 3 ? "Alice" : "Bob",
                AuthorEmail = i < 3 ? "alice@test.com" : "bob@test.com",
                CommittedAt = DateTime.UtcNow.AddDays(-i * 10),
                CreatedAt = DateTime.UtcNow
            });
        }

        // Add 3 older commits (3-6 months ago)
        for (int i = 0; i < 3; i++)
        {
            _context.Commits.Add(new Commit
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                Sha = $"old{i:D4}",
                Message = $"old commit {i}",
                AuthorName = "Charlie",
                AuthorEmail = "charlie@test.com",
                CommittedAt = DateTime.UtcNow.AddMonths(-4).AddDays(-i * 10),
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var service = CreateService();
        var velocity = await service.CalculateVelocityAsync(repo.Id, CancellationToken.None);

        velocity.CommitsPerMonth.Should().Be(2.0); // 6 commits / 3 months
        velocity.ActiveContributors.Should().Be(2); // Alice and Bob
        velocity.Trend.Should().Be("increasing"); // 2.0/month > 1.0/month * 1.2
    }

    [Fact]
    public async Task CalculateVelocityAsync_NoCommits_ReturnsZeros()
    {
        var repo = new Repository { Id = Guid.NewGuid(), Name = "empty-repo", Url = "https://github.com/test/empty", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _context.Repositories.Add(repo);
        await _context.SaveChangesAsync();

        var service = CreateService();
        var velocity = await service.CalculateVelocityAsync(repo.Id, CancellationToken.None);

        velocity.CommitsPerMonth.Should().Be(0);
        velocity.ActiveContributors.Should().Be(0);
        velocity.Trend.Should().Be("stable");
    }

    private ReportService CreateService()
    {
        return new ReportService(
            _context,
            _apiKeyResolverMock.Object,
            _llmOptions,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }
}
