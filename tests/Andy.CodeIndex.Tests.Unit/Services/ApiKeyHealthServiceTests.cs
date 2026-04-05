using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class ApiKeyHealthServiceTests
{
    private readonly Mock<IApiKeyResolver> _resolverMock = new();
    private readonly ApiKeyHealthStatus _status = new();

    private ApiKeyHealthService CreateService()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();

        serviceProvider.Setup(sp => sp.GetService(typeof(IApiKeyResolver)))
            .Returns(_resolverMock.Object);

        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new ApiKeyHealthService(
            scopeFactory.Object,
            _status,
            NullLogger<ApiKeyHealthService>.Instance);
    }

    [Fact]
    public async Task CheckKeys_BothValid_SetsStatusCorrectly()
    {
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-valid-llm", "https://api.openai.com/v1", "gpt-4o", "user"));
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-valid-embed", "https://api.openai.com/v1", "text-embedding-3-small", "system"));

        var service = CreateService();
        await service.CheckKeysAsync(CancellationToken.None);

        _status.LlmKeyValid.Should().BeTrue();
        _status.EmbeddingKeyValid.Should().BeTrue();
        _status.LlmError.Should().BeNull();
        _status.EmbeddingError.Should().BeNull();
        _status.LastChecked.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CheckKeys_NoLlmKey_SetsInvalid()
    {
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as string, "https://api.openai.com/v1", "gpt-4o", "none"));
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-valid", "https://api.openai.com/v1", "text-embedding-3-small", "system"));

        var service = CreateService();
        await service.CheckKeysAsync(CancellationToken.None);

        _status.LlmKeyValid.Should().BeFalse();
        _status.LlmError.Should().Be("No LLM API key configured");
        _status.EmbeddingKeyValid.Should().BeTrue();
    }

    [Fact]
    public async Task CheckKeys_NoEmbeddingKey_SetsInvalid()
    {
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-valid", "https://api.openai.com/v1", "gpt-4o", "user"));
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as string, "https://api.openai.com/v1", "text-embedding-3-small", "none"));

        var service = CreateService();
        await service.CheckKeysAsync(CancellationToken.None);

        _status.LlmKeyValid.Should().BeTrue();
        _status.EmbeddingKeyValid.Should().BeFalse();
        _status.EmbeddingError.Should().Be("No embedding API key configured");
    }

    [Fact]
    public async Task CheckKeys_ResolverThrows_SetsError()
    {
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Decryption failed"));
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Key store unavailable"));

        var service = CreateService();
        await service.CheckKeysAsync(CancellationToken.None);

        _status.LlmKeyValid.Should().BeFalse();
        _status.LlmError.Should().Be("Decryption failed");
        _status.EmbeddingKeyValid.Should().BeFalse();
        _status.EmbeddingError.Should().Be("Key store unavailable");
    }

    [Fact]
    public async Task CheckKeys_BothEmpty_SetsInvalid()
    {
        _resolverMock.Setup(r => r.ResolveLlmKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "https://api.openai.com/v1", "gpt-4o", "none"));
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("", "https://api.openai.com/v1", "text-embedding-3-small", "none"));

        var service = CreateService();
        await service.CheckKeysAsync(CancellationToken.None);

        _status.LlmKeyValid.Should().BeFalse();
        _status.EmbeddingKeyValid.Should().BeFalse();
    }
}
