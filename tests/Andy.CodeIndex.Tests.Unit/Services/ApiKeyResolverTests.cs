using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class ApiKeyResolverTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IEncryptionService> _encryptionMock = new();

    public ApiKeyResolverTests()
    {
        _context = TestDbContextFactory.Create();

        _encryptionMock.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => $"enc:{s}");
        _encryptionMock.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.StartsWith("enc:") ? s[4..] : "");
    }

    public void Dispose() => _context.Dispose();

    private ApiKeyResolver CreateResolver(string? systemKey = null, string? systemLlmKey = null)
    {
        var embeddingOptions = Options.Create(new EmbeddingOptions { ApiKey = systemKey });
        var llmOptions = Options.Create(new EnrichmentLlmOptions { ApiKey = systemLlmKey });
        return new ApiKeyResolver(_context, _encryptionMock.Object, embeddingOptions, llmOptions, NullLogger<ApiKeyResolver>.Instance);
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_UserKeySet_ReturnsUserKey()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-1",
            EmbeddingApiKey = "enc:sk-user-key-123",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemKey: "sk-system-key");
        var (key, source) = await resolver.ResolveEmbeddingKeyAsync("user-1");

        key.Should().Be("sk-user-key-123");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_NoUserKey_FallsBackToSystem()
    {
        var resolver = CreateResolver(systemKey: "sk-system-key");
        var (key, source) = await resolver.ResolveEmbeddingKeyAsync("user-no-settings");

        key.Should().Be("sk-system-key");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_NoKeys_ReturnsNone()
    {
        var resolver = CreateResolver(systemKey: null);
        var (key, source) = await resolver.ResolveEmbeddingKeyAsync("user-no-settings");

        key.Should().BeNull();
        source.Should().Be("none");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_NoUserId_FallsBackToSystem()
    {
        var resolver = CreateResolver(systemKey: "sk-system-key");
        var (key, source) = await resolver.ResolveEmbeddingKeyAsync(null);

        key.Should().Be("sk-system-key");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_UserKeyCorrupt_FallsBackToSystem()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-corrupt",
            EmbeddingApiKey = "not-encrypted",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemKey: "sk-system-key");
        var (key, source) = await resolver.ResolveEmbeddingKeyAsync("user-corrupt");

        // Decrypt returns "" for non-encrypted data → falls back
        key.Should().Be("sk-system-key");
        source.Should().Be("system");
    }

    // --- LLM Key Resolution Tests ---

    [Fact]
    public async Task ResolveLlmKeyAsync_UserLlmKey_ReturnsUserKey()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-llm",
            LlmApiKey = "enc:sk-user-llm-key",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemLlmKey: "sk-system-llm");
        var (key, model, source) = await resolver.ResolveLlmKeyAsync("user-llm");

        key.Should().Be("sk-user-llm-key");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoUserLlm_FallsBackToUserEmbedding()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-embed-only",
            EmbeddingApiKey = "enc:sk-embed-key",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver();
        var (key, _, source) = await resolver.ResolveLlmKeyAsync("user-embed-only");

        key.Should().Be("sk-embed-key");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoUserKey_FallsBackToSystemLlm()
    {
        var resolver = CreateResolver(systemLlmKey: "sk-system-llm");
        var (key, _, source) = await resolver.ResolveLlmKeyAsync("user-none");

        key.Should().Be("sk-system-llm");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoLlmKey_FallsBackToSystemEmbedding()
    {
        var resolver = CreateResolver(systemKey: "sk-embed-system");
        var (key, _, source) = await resolver.ResolveLlmKeyAsync("user-none");

        key.Should().Be("sk-embed-system");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoKeys_ReturnsNone()
    {
        var resolver = CreateResolver();
        var (key, _, source) = await resolver.ResolveLlmKeyAsync("user-none");

        key.Should().BeNull();
        source.Should().Be("none");
    }
}
