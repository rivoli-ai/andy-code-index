using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.CodeIndex.Tests.Integration;

/// <summary>
/// SM.2.9 — Per-branch indexing status with pre-seeded branch data.
/// Uses a fresh factory per test so the in-memory DB can be seeded directly.
/// </summary>
public class Sm29BranchStatusSeedTests
{
    [Fact]
    public async Task GetBranchStatus_SeededDefaultBranch_Returns200_WithExpectedShape()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        // Create a repository via the API
        var url = $"https://github.com/test/branch-seed-{Guid.NewGuid()}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = url });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var repo = (await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options))!;

        // Seed a branch row directly into the in-memory DB
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();
        db.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Name = "main",
            HeadCommitSha = "abc1234def5678",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Call the per-branch status endpoint
        var response = await client.GetAsync(
            $"/api/v1/repositories/{repo.Id}/branches/main/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("branch", out var branchProp).Should().BeTrue();
        branchProp.GetString().Should().Be("main");

        root.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("headCommitSha", out var headShaProp).Should().BeTrue();
        headShaProp.GetString().Should().Be("abc1234def5678");

        root.TryGetProperty("progress", out _).Should().BeTrue(
            "progress field must be present (may be null when no task is running)");
    }

    [Fact]
    public async Task GetBranchStatus_SeededNonDefaultBranch_Returns200_HeadShaIsUsed()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        var url = $"https://github.com/test/branch-nondefault-{Guid.NewGuid()}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = url });
        var repo = (await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options))!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();

        // Seed default branch
        db.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Name = "main",
            HeadCommitSha = "aaabbbccc111",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });

        // Seed a feature branch
        db.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Name = "feature/auth",
            HeadCommitSha = "featuresha111",
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Query the feature branch
        var response = await client.GetAsync(
            $"/api/v1/repositories/{repo.Id}/branches/feature/auth/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("branch").GetString().Should().Be("feature/auth");
        root.GetProperty("headCommitSha").GetString().Should().Be("featuresha111");
    }

    [Fact]
    public async Task GetBranchStatus_TwoBranchesConcurrently_ReturnIndependentState()
    {
        // Verifies that two branches don't bleed state into each other
        // (no synthetic-default-branch hazard).
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        var url = $"https://github.com/test/branch-indep-{Guid.NewGuid()}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/repositories",
            new CreateRepositoryRequest { Url = url });
        var repo = (await createResponse.Content.ReadFromJsonAsync<RepositoryDto>(TestJson.Options))!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();

        db.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id,
            Name = "main", HeadCommitSha = "main-sha-111", IsDefault = true, CreatedAt = DateTime.UtcNow
        });
        db.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id,
            Name = "develop", HeadCommitSha = "develop-sha-222", IsDefault = false, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mainResponse = await client.GetAsync(
            $"/api/v1/repositories/{repo.Id}/branches/main/status");
        var developResponse = await client.GetAsync(
            $"/api/v1/repositories/{repo.Id}/branches/develop/status");

        mainResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        developResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var mainBody = JsonDocument.Parse(await mainResponse.Content.ReadAsStringAsync()).RootElement;
        var developBody = JsonDocument.Parse(await developResponse.Content.ReadAsStringAsync()).RootElement;

        mainBody.GetProperty("branch").GetString().Should().Be("main");
        mainBody.GetProperty("headCommitSha").GetString().Should().Be("main-sha-111");

        developBody.GetProperty("branch").GetString().Should().Be("develop");
        developBody.GetProperty("headCommitSha").GetString().Should().Be("develop-sha-222");

        // Confirm the SHAs are distinct — no cross-branch bleed
        mainBody.GetProperty("headCommitSha").GetString()
            .Should().NotBe(developBody.GetProperty("headCommitSha").GetString());
    }
}
