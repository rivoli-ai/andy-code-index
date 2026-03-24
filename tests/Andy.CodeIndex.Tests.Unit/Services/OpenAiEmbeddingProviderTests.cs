using System.Net;
using System.Text.Json;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class OpenAiEmbeddingProviderTests
{
    private static OpenAiEmbeddingProvider CreateProvider(HttpMessageHandler handler, EmbeddingOptions? options = null)
    {
        var opts = options ?? new EmbeddingOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "text-embedding-3-small",
            ApiKey = "test-key",
            Dimensions = 1536,
            MaxRetries = 2,
            TimeoutSeconds = 5
        };

        // Mock resolver that returns the API key from options
        var resolverMock = new Moq.Mock<IApiKeyResolver>();
        resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(Moq.It.IsAny<string?>(), Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<(string?, string)>((opts.ApiKey ?? "test-key", "test")));

        var httpClient = new HttpClient(handler);
        return new OpenAiEmbeddingProvider(
            httpClient,
            Options.Create(opts),
            resolverMock.Object,
            NullLogger<OpenAiEmbeddingProvider>.Instance);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_Success_ReturnsEmbeddings()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { index = 0, embedding = new[] { 0.1f, 0.2f, 0.3f } },
                new { index = 1, embedding = new[] { 0.4f, 0.5f, 0.6f } }
            }
        });

        var handler = new MockHttpHandler(HttpStatusCode.OK, responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GenerateEmbeddingsAsync(["hello", "world"]);

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new[] { 0.1f, 0.2f, 0.3f });
        result[1].Should().BeEquivalentTo(new[] { 0.4f, 0.5f, 0.6f });
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_EmptyInput_ReturnsEmpty()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler);

        var result = await provider.GenerateEmbeddingsAsync([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ResultsOrderedByIndex()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { index = 1, embedding = new[] { 0.4f, 0.5f } },
                new { index = 0, embedding = new[] { 0.1f, 0.2f } }
            }
        });

        var handler = new MockHttpHandler(HttpStatusCode.OK, responseJson);
        var provider = CreateProvider(handler);

        var result = await provider.GenerateEmbeddingsAsync(["a", "b"]);

        // Should be reordered by index
        result[0].Should().BeEquivalentTo(new[] { 0.1f, 0.2f });
        result[1].Should().BeEquivalentTo(new[] { 0.4f, 0.5f });
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ServerError_RetriesAndThrows()
    {
        var callCount = 0;
        var handler = new MockHttpHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error")
            };
        });

        var provider = CreateProvider(handler, new EmbeddingOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "test",
            MaxRetries = 2,
            TimeoutSeconds = 5
        });

        var act = () => provider.GenerateEmbeddingsAsync(["test"]);
        await act.Should().ThrowAsync<HttpRequestException>();

        callCount.Should().Be(3); // 1 original + 2 retries
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_RateLimited_RetriesWithBackoff()
    {
        var callCount = 0;
        var handler = new MockHttpHandler(_ =>
        {
            callCount++;
            if (callCount <= 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limited")
                };
            }

            var json = JsonSerializer.Serialize(new
            {
                data = new[] { new { index = 0, embedding = new[] { 0.1f } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler);
        var result = await provider.GenerateEmbeddingsAsync(["test"]);

        result.Should().HaveCount(1);
        callCount.Should().Be(2); // 1 rate limited + 1 success
    }

    [Fact]
    public void Dimensions_ReturnsModelDimensions()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler, new EmbeddingOptions { Model = "text-embedding-3-large" });
        provider.Dimensions.Should().Be(3072);
    }

    [Fact]
    public void Dimensions_FallsBackToExplicitForUnknownModel()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler, new EmbeddingOptions { Model = "custom-model", Dimensions = 768 });
        provider.Dimensions.Should().Be(768);
    }

    [Fact]
    public void IsAvailable_TrueWhenApiKeySet()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler);
        provider.IsAvailable.Should().BeTrue(); // test-key is set in CreateProvider
    }

    [Fact]
    public void IsAvailable_FalseWhenNoApiKey()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler, new EmbeddingOptions { ApiKey = null });
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void ModelName_ReturnsConfiguredModel()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler);
        provider.ModelName.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SetsAuthorizationHeader()
    {
        string? authHeader = null;
        var handler = new MockHttpHandler(request =>
        {
            authHeader = request.Headers.Authorization?.ToString();
            var json = JsonSerializer.Serialize(new
            {
                data = new[] { new { index = 0, embedding = new[] { 0.1f } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var provider = CreateProvider(handler);
        await provider.GenerateEmbeddingsAsync(["test"]);

        authHeader.Should().Be("Bearer test-key");
    }

    /// <summary>Mock HTTP handler for testing.</summary>
    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpHandler(HttpStatusCode statusCode, string content)
        {
            _handler = _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
        }

        public MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
