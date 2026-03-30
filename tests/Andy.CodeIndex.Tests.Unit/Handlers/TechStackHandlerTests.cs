using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class TechStackHandlerTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<IApiKeyResolver> _resolverMock = new();
    private readonly Mock<IHttpClientFactory> _httpFactoryMock = new();
    private readonly TechStackHandler _handler;
    private readonly Repository _testRepo;

    public TechStackHandlerTests()
    {
        _context = TestDbContextFactory.Create();
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((string?)null, "https://api.openai.com/v1", "gpt-4", "none"));

        _handler = new TechStackHandler(
            _context, _resolverMock.Object,
            Options.Create(new EnrichmentLlmOptions()),
            _httpFactoryMock.Object,
            _gitServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<TechStackHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test-repo", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsCreateTechStack()
    {
        _handler.Operation.Should().Be(TaskOperation.CreateTechStack);
    }

    [Fact]
    public async Task HandleAsync_SkipsWhenNoLlmKey()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateTechStack, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        // No enrichments should be created
        var enrichments = _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.TechStack)
            .ToList();
        enrichments.Should().BeEmpty();
    }

    [Fact]
    public void BuildLanguageBreakdown_ReturnsMarkdownTable()
    {
        var files = new List<GitFileInfo>
        {
            new() { Path = "src/Program.cs", Language = "C#", Size = 100 },
            new() { Path = "src/Service.cs", Language = "C#", Size = 200 },
            new() { Path = "client/app.ts", Language = "TypeScript", Size = 150 },
            new() { Path = "README.md", Language = null, Size = 50 }
        };

        var result = TechStackHandler.BuildLanguageBreakdown(files);

        result.Should().Contain("C#");
        result.Should().Contain("TypeScript");
        result.Should().Contain("50%"); // C# = 2/4
        result.Should().Contain("25%"); // TypeScript = 1/4
        result.Should().Contain("Total files: 4");
    }

    [Fact]
    public void BuildLanguageBreakdown_EmptyFiles_ReturnsNoFiles()
    {
        var result = TechStackHandler.BuildLanguageBreakdown([]);
        result.Should().Contain("No files found");
    }

    [Fact]
    public void MatchesAnyPattern_ExtensionPattern()
    {
        TechStackHandler.MatchesAnyPattern("src/MyProject.csproj", ["*.csproj"]).Should().BeTrue();
        TechStackHandler.MatchesAnyPattern("src/app.ts", ["*.csproj"]).Should().BeFalse();
    }

    [Fact]
    public void MatchesAnyPattern_ExactFilename()
    {
        TechStackHandler.MatchesAnyPattern("package.json", ["package.json"]).Should().BeTrue();
        TechStackHandler.MatchesAnyPattern("src/package.json", ["package.json"]).Should().BeTrue();
        TechStackHandler.MatchesAnyPattern("Dockerfile", ["Dockerfile"]).Should().BeTrue();
    }

    [Fact]
    public void MatchesAnyPattern_DirectoryPattern()
    {
        TechStackHandler.MatchesAnyPattern(".github/workflows/ci.yml", [".github/workflows/*.yml"]).Should().BeTrue();
        TechStackHandler.MatchesAnyPattern(".github/workflows/deploy.yaml", [".github/workflows/*.yaml"]).Should().BeTrue();
        TechStackHandler.MatchesAnyPattern("src/main.yml", [".github/workflows/*.yml"]).Should().BeFalse();
    }
}

public class ReportServiceTechStackTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;

    public ReportServiceTechStackTests()
    {
        _context = TestDbContextFactory.Create();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void ExtractTechComponents_ParsesBackend()
    {
        var content = """
            ## Backend
            - .NET 8 with ASP.NET Core
            - Entity Framework Core

            ## Frontend
            - Angular 17.2
            """;

        var backend = ReportService.ExtractTechComponents(content, "## Backend");
        backend.Should().Contain(c => c.Name == ".NET" && c.Version == "8");

        var frontend = ReportService.ExtractTechComponents(content, "## Frontend");
        frontend.Should().Contain(c => c.Name == "Angular" && c.Version == "17.2");
    }

    [Fact]
    public void ExtractTechComponents_ParsesDatabase()
    {
        var content = """
            ## Database
            - PostgreSQL 15.2
            - Redis for caching

            ## Infrastructure
            - Docker
            - Kubernetes
            - GitHub Actions CI/CD
            """;

        var db = ReportService.ExtractTechComponents(content, "## Database");
        db.Should().Contain(c => c.Name == "PostgreSQL" && c.Version == "15.2");
        db.Should().Contain(c => c.Name == "Redis");

        var infra = ReportService.ExtractTechComponents(content, "## Infrastructure");
        infra.Should().Contain(c => c.Name == "Docker");
        infra.Should().Contain(c => c.Name == "Kubernetes");
        infra.Should().Contain(c => c.Name == "GitHub Actions");
    }

    [Fact]
    public void ExtractTechComponents_ReturnsEmptyForMissingSection()
    {
        var content = "## Backend\n- .NET 8\n";
        var result = ReportService.ExtractTechComponents(content, "## Database");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractVersion_FindsVersionAfterKeyword()
    {
        ReportService.ExtractVersion(".NET 8.0", ".NET").Should().Be("8.0");
        ReportService.ExtractVersion("Angular v17.2.1", "Angular").Should().Be("17.2.1");
        ReportService.ExtractVersion("Docker is used", "Docker").Should().BeNull();
        ReportService.ExtractVersion("PostgreSQL 15", "PostgreSQL").Should().Be("15");
    }
}
