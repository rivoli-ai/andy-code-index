using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Discovery;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using System.Text.Json;

namespace Andy.CodeIndex.Tests.Unit.Discovery;

public class DiscoveryInputTests
{
    private readonly Mock<ICodeRepositoryRepository> _repoRepoMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();

    public DiscoveryInputTests()
    {
        _repoRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    [Theory]
    [InlineData("rivoli-ai", "rivoli-ai")]
    [InlineData("https://github.com/rivoli-ai", "rivoli-ai")]
    [InlineData("https://github.com/rivoli-ai/", "rivoli-ai")]
    [InlineData("github.com/rivoli-ai", "rivoli-ai")]
    [InlineData("  rivoli-ai  ", "rivoli-ai")]
    public void ParseGitHubOrg_ExtractsOrgName(string input, string expected)
    {
        RepoDiscoveryService.ParseGitHubOrg(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("myorg", "myorg")]
    [InlineData("https://dev.azure.com/myorg", "myorg")]
    [InlineData("https://dev.azure.com/myorg/", "myorg")]
    [InlineData("dev.azure.com/myorg", "myorg")]
    [InlineData("  myorg  ", "myorg")]
    public void ParseAzureDevOpsOrg_ExtractsOrgName(string input, string expected)
    {
        RepoDiscoveryService.ParseAzureDevOpsOrg(input).Should().Be(expected);
    }

    [Fact]
    public async Task DiscoverGitHub_WithFullUrl_ExtractsOrgAndWorks()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "repo1", full_name = "rivoli-ai/repo1", clone_url = "https://github.com/rivoli-ai/repo1.git",
                  default_branch = "main", archived = false, fork = false, disabled = false }
        });

        string? capturedUrl = null;
        var handler = new MockHandler(request =>
        {
            capturedUrl = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        _httpClientFactoryMock.Setup(f => f.CreateClient("Discovery")).Returns(new HttpClient(handler));
        var service = new RepoDiscoveryService(_httpClientFactoryMock.Object, _repoRepoMock.Object,
            NullLogger<RepoDiscoveryService>.Instance);

        // Pass full URL — should extract "rivoli-ai" and call GitHub API correctly
        var repos = await service.DiscoverGitHubAsync("https://github.com/rivoli-ai");

        repos.Should().HaveCount(1);
        capturedUrl.Should().Contain("/orgs/rivoli-ai/repos");
        capturedUrl.Should().NotContain("github.com/rivoli-ai"); // Should NOT embed the full URL
    }

    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_handler(request));
    }
}
