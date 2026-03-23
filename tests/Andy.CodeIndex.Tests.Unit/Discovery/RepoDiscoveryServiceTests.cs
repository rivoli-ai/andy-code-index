using System.Net;
using System.Text.Json;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Discovery;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Andy.CodeIndex.Tests.Unit.Discovery;

public class RepoDiscoveryServiceTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();

    public RepoDiscoveryServiceTests()
    {
        _repoRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private RepoDiscoveryService CreateService(HttpMessageHandler handler)
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient("Discovery"))
            .Returns(new HttpClient(handler));
        return new RepoDiscoveryService(
            _httpClientFactoryMock.Object,
            _repoRepoMock.Object,
            NullLogger<RepoDiscoveryService>.Instance);
    }

    // --- GitHub Tests ---

    [Fact]
    public async Task DiscoverGitHub_ReturnsRepos()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "andy-docs", full_name = "rivoli-ai/andy-docs",
                  clone_url = "https://github.com/rivoli-ai/andy-docs.git",
                  default_branch = "main", archived = false, fork = false, disabled = false },
            new { name = "andy-auth", full_name = "rivoli-ai/andy-auth",
                  clone_url = "https://github.com/rivoli-ai/andy-auth.git",
                  default_branch = "main", archived = false, fork = false, disabled = false }
        });
        var handler = new MockHandler(HttpStatusCode.OK, json);
        var service = CreateService(handler);

        var repos = await service.DiscoverGitHubAsync("rivoli-ai");

        repos.Should().HaveCount(2);
        repos[0].Name.Should().Be("andy-docs");
        repos[0].Provider.Should().Be("GitHub");
        repos[0].CloneUrl.Should().Contain("github.com");
    }

    [Fact]
    public async Task DiscoverGitHub_ExcludesArchived()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "active", full_name = "org/active", clone_url = "https://github.com/org/active.git",
                  default_branch = "main", archived = false, fork = false, disabled = false },
            new { name = "old", full_name = "org/old", clone_url = "https://github.com/org/old.git",
                  default_branch = "main", archived = true, fork = false, disabled = false }
        });
        var service = CreateService(new MockHandler(HttpStatusCode.OK, json));

        var repos = await service.DiscoverGitHubAsync("org", excludeArchived: true);

        repos.Should().HaveCount(1);
        repos[0].Name.Should().Be("active");
    }

    [Fact]
    public async Task DiscoverGitHub_ExcludesForks()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "original", full_name = "org/original", clone_url = "https://github.com/org/original.git",
                  default_branch = "main", archived = false, fork = false, disabled = false },
            new { name = "forked", full_name = "org/forked", clone_url = "https://github.com/org/forked.git",
                  default_branch = "main", archived = false, fork = true, disabled = false }
        });
        var service = CreateService(new MockHandler(HttpStatusCode.OK, json));

        var repos = await service.DiscoverGitHubAsync("org", excludeForks: true);

        repos.Should().HaveCount(1);
        repos[0].Name.Should().Be("original");
    }

    [Fact]
    public async Task DiscoverGitHub_MarksAlreadyTracked()
    {
        _repoRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Repository { Id = Guid.NewGuid(), Name = "andy-docs",
                    Url = "https://github.com/rivoli-ai/andy-docs.git",
                    Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            ]);

        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "andy-docs", full_name = "rivoli-ai/andy-docs",
                  clone_url = "https://github.com/rivoli-ai/andy-docs.git",
                  default_branch = "main", archived = false, fork = false, disabled = false },
            new { name = "andy-auth", full_name = "rivoli-ai/andy-auth",
                  clone_url = "https://github.com/rivoli-ai/andy-auth.git",
                  default_branch = "main", archived = false, fork = false, disabled = false }
        });
        var service = CreateService(new MockHandler(HttpStatusCode.OK, json));

        var repos = await service.DiscoverGitHubAsync("rivoli-ai");

        repos.Should().HaveCount(2);
        repos.First(r => r.Name == "andy-docs").AlreadyTracked.Should().BeTrue();
        repos.First(r => r.Name == "andy-auth").AlreadyTracked.Should().BeFalse();
    }

    // --- Azure DevOps Tests ---

    [Fact]
    public async Task DiscoverAzureDevOps_ReturnsRepos()
    {
        var callCount = 0;
        var handler = new MockHandler(request =>
        {
            callCount++;
            if (request.RequestUri!.PathAndQuery.Contains("_apis/projects"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        value = new[] { new { name = "MyProject" } }
                    }), System.Text.Encoding.UTF8, "application/json")
                };
            }
            // git/repositories
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    value = new[]
                    {
                        new { name = "webapp", remoteUrl = "https://dev.azure.com/myorg/MyProject/_git/webapp",
                              defaultBranch = "refs/heads/main", isDisabled = false },
                        new { name = "disabled-repo", remoteUrl = "https://dev.azure.com/myorg/MyProject/_git/disabled",
                              defaultBranch = "refs/heads/main", isDisabled = true }
                    }
                }), System.Text.Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler);

        var repos = await service.DiscoverAzureDevOpsAsync("myorg");

        repos.Should().HaveCount(1); // disabled filtered out
        repos[0].Name.Should().Be("webapp");
        repos[0].FullName.Should().Be("myorg/MyProject/webapp");
        repos[0].Provider.Should().Be("AzureDevOps");
        repos[0].DefaultBranch.Should().Be("main"); // refs/heads/ stripped
    }

    [Fact]
    public async Task DiscoverAzureDevOps_WithProject_SkipsProjectListing()
    {
        var handler = new MockHandler(request =>
        {
            // Should go straight to repos, not projects
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    value = new[]
                    {
                        new { name = "api", remoteUrl = "https://dev.azure.com/org/proj/_git/api",
                              defaultBranch = (string?)null, isDisabled = false }
                    }
                }), System.Text.Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler);

        var repos = await service.DiscoverAzureDevOpsAsync("org", project: "proj");

        repos.Should().HaveCount(1);
        repos[0].FullName.Should().Be("org/proj/api");
    }

    [Fact]
    public async Task DiscoverAzureDevOps_UsesBasicAuthWithPat()
    {
        string? authHeader = null;
        var handler = new MockHandler(request =>
        {
            authHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler);

        await service.DiscoverAzureDevOpsAsync("org", project: "proj", pat: "my-pat-token");

        authHeader.Should().StartWith("Basic ");
        var decoded = System.Text.Encoding.ASCII.GetString(
            Convert.FromBase64String(authHeader!.Replace("Basic ", "")));
        decoded.Should().Be(":my-pat-token");
    }

    // --- Mock Handler ---
    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHandler(HttpStatusCode status, string content)
        {
            _handler = _ => new HttpResponseMessage(status)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
        }

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_handler(request));
    }
}
