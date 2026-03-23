using System.Net;
using System.Net.Http.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.CodeIndex.Tests.Integration;

public class IndexingApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public IndexingApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSyncStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/sync/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<SyncStatusDto>();
        status.Should().NotBeNull();
        status!.IntervalSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetHistory_NonExistentRepo_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/repositories/{Guid.NewGuid()}/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHistory_ExistingRepo_Returns200()
    {
        // Create a repo first
        var createResponse = await _client.PostAsJsonAsync("/api/v1/repositories",
            new { url = "https://github.com/test/history-" + Guid.NewGuid() });
        var repo = await createResponse.Content.ReadFromJsonAsync<RepositoryDto>();

        var response = await _client.GetAsync($"/api/v1/repositories/{repo!.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHistory_WithIndexingRuns_ReturnsRuns()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();

        var repo = new Repository
        {
            Id = Guid.NewGuid(), Name = "history-test",
            Url = "https://github.com/test/hist-" + Guid.NewGuid(),
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        context.Repositories.Add(repo);
        context.IndexingRuns.Add(new IndexingRun
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow,
            Status = "completed",
            SnippetsAdded = 100, SnippetsUpdated = 5, SnippetsDeleted = 2, SnippetsUnchanged = 500,
            ApiDocsGenerated = 20, CommitsScanned = 10,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/v1/repositories/{repo.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var runs = await response.Content.ReadFromJsonAsync<List<IndexingRunDto>>();
        runs.Should().HaveCount(1);
        runs![0].SnippetsAdded.Should().Be(100);
        runs[0].DurationSeconds.Should().BeGreaterThan(0);
        runs[0].Status.Should().Be("completed");
    }
}
