using System.Net;
using System.Net.Http.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.CodeIndex.Tests.Integration;

public class SearchApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public SearchApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // Seed data once
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();

        if (!context.Repositories.Any(r => r.Name == "search-test"))
        {
            var repo = new Repository
            {
                Id = Guid.NewGuid(), Name = "search-test",
                Url = "https://github.com/t/search-" + Guid.NewGuid(),
                Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            context.Repositories.Add(repo);
            context.Enrichments.AddRange(
                new Enrichment
                {
                    Id = Guid.NewGuid(), RepositoryId = repo.Id,
                    Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk,
                    Content = "public class UserService implements authentication logic",
                    Language = "csharp", FilePath = "UserService.cs",
                    StartLine = 1, EndLine = 10, CreatedAt = DateTime.UtcNow
                },
                new Enrichment
                {
                    Id = Guid.NewGuid(), RepositoryId = repo.Id,
                    Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk,
                    Content = "def calculate_total(items): return sum(item.price for item in items)",
                    Language = "python", FilePath = "calc.py",
                    StartLine = 1, EndLine = 5, CreatedAt = DateTime.UtcNow
                });
            context.SaveChanges();
        }
    }

    [Fact]
    public async Task KeywordSearch_MatchesContent_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/search/keyword?keywords=UserService");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        results.Should().NotBeNull();
        results!.SearchMode.Should().Be("keyword");
        results.Results.Should().Contain(r => r.Content.Contains("UserService"));
    }

    [Fact]
    public async Task KeywordSearch_NoMatch_ReturnsEmpty()
    {
        var response = await _client.GetAsync("/api/v1/search/keyword?keywords=nonexistentxyz123");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        results!.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task HybridSearch_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/search", new { query = "authentication", limit = 5 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await response.Content.ReadFromJsonAsync<SearchResultsDto>();
        results!.SearchMode.Should().Be("hybrid");
    }

    [Fact]
    public async Task EnrichmentQuery_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/enrichments?type=Development&limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
