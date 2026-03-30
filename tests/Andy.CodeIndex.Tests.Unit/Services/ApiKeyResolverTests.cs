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

    private ApiKeyResolver CreateResolver(
        string? systemKey = null, string? systemLlmKey = null,
        string? systemEmbeddingBaseUrl = null, string? systemLlmBaseUrl = null)
    {
        var embeddingOptions = Options.Create(new EmbeddingOptions
        {
            ApiKey = systemKey,
            BaseUrl = systemEmbeddingBaseUrl ?? "https://api.openai.com/v1"
        });
        var llmOptions = Options.Create(new EnrichmentLlmOptions
        {
            ApiKey = systemLlmKey,
            BaseUrl = systemLlmBaseUrl ?? "https://api.openai.com/v1"
        });
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
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-1");

        key.Should().Be("sk-user-key-123");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_NoUserKey_FallsBackToSystem()
    {
        var resolver = CreateResolver(systemKey: "sk-system-key");
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-no-settings");

        key.Should().Be("sk-system-key");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_NoKeys_ReturnsNone()
    {
        var resolver = CreateResolver(systemKey: null);
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-no-settings");

        key.Should().BeNull();
        source.Should().Be("none");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_NoUserId_FallsBackToSystem()
    {
        var resolver = CreateResolver(systemKey: "sk-system-key");
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync(null);

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
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-corrupt");

        // Decrypt returns "" for non-encrypted data -> falls back
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
        var (key, baseUrl, model, source) = await resolver.ResolveLlmKeyAsync("user-llm");

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
        var (key, _, _, source) = await resolver.ResolveLlmKeyAsync("user-embed-only");

        key.Should().Be("sk-embed-key");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoUserKey_FallsBackToSystemLlm()
    {
        var resolver = CreateResolver(systemLlmKey: "sk-system-llm");
        var (key, _, _, source) = await resolver.ResolveLlmKeyAsync("user-none");

        key.Should().Be("sk-system-llm");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoLlmKey_FallsBackToSystemEmbedding()
    {
        var resolver = CreateResolver(systemKey: "sk-embed-system");
        var (key, _, _, source) = await resolver.ResolveLlmKeyAsync("user-none");

        key.Should().Be("sk-embed-system");
        source.Should().Be("system");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_NoKeys_ReturnsNone()
    {
        var resolver = CreateResolver();
        var (key, _, _, source) = await resolver.ResolveLlmKeyAsync("user-none");

        key.Should().BeNull();
        source.Should().Be("none");
    }

    // --- Base URL Resolution Tests ---

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_ReturnsUserBaseUrl_WhenConfigured()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-url",
            EmbeddingApiKey = "enc:sk-user-key",
            EmbeddingBaseUrl = "http://localhost:11434/v1",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemKey: "sk-system", systemEmbeddingBaseUrl: "https://api.openai.com/v1");
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-url");

        key.Should().Be("sk-user-key");
        baseUrl.Should().Be("http://localhost:11434/v1");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_FallsBackToSystemUrl_WhenUserUrlNull()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-no-url",
            EmbeddingApiKey = "enc:sk-user-key",
            EmbeddingBaseUrl = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemKey: "sk-system", systemEmbeddingBaseUrl: "https://api.openai.com/v1");
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-no-url");

        key.Should().Be("sk-user-key");
        baseUrl.Should().Be("https://api.openai.com/v1");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_ReturnsUserBaseUrl_WhenConfigured()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-llm-url",
            LlmApiKey = "enc:sk-user-llm-key",
            LlmBaseUrl = "https://api.groq.com/openai/v1",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemLlmKey: "sk-system-llm", systemLlmBaseUrl: "https://api.openai.com/v1");
        var (key, baseUrl, model, source) = await resolver.ResolveLlmKeyAsync("user-llm-url");

        key.Should().Be("sk-user-llm-key");
        baseUrl.Should().Be("https://api.groq.com/openai/v1");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_FallsBackToSystemUrl_WhenUserUrlNull()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-llm-no-url",
            LlmApiKey = "enc:sk-user-llm-key",
            LlmBaseUrl = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemLlmKey: "sk-system-llm", systemLlmBaseUrl: "https://api.openai.com/v1");
        var (key, baseUrl, model, source) = await resolver.ResolveLlmKeyAsync("user-llm-no-url");

        key.Should().Be("sk-user-llm-key");
        baseUrl.Should().Be("https://api.openai.com/v1");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveEmbeddingKeyAsync_ReturnsAllFields_KeyUrlModel()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-all-fields",
            EmbeddingApiKey = "enc:sk-full-key",
            EmbeddingBaseUrl = "http://localhost:11434/v1",
            EmbeddingModel = "nomic-embed-text",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemKey: "sk-system");
        var (key, baseUrl, model, source) = await resolver.ResolveEmbeddingKeyAsync("user-all-fields");

        key.Should().Be("sk-full-key");
        baseUrl.Should().Be("http://localhost:11434/v1");
        model.Should().Be("nomic-embed-text");
        source.Should().Be("user");
    }

    [Fact]
    public async Task ResolveLlmKeyAsync_ReturnsAllFields_KeyUrlModel()
    {
        _context.UserSettings.Add(new UserSettings
        {
            Id = Guid.NewGuid(), UserId = "user-llm-all",
            LlmApiKey = "enc:sk-llm-full",
            LlmBaseUrl = "https://api.groq.com/openai/v1",
            LlmModel = "llama-3-70b",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var resolver = CreateResolver(systemLlmKey: "sk-system-llm");
        var (key, baseUrl, model, source) = await resolver.ResolveLlmKeyAsync("user-llm-all");

        key.Should().Be("sk-llm-full");
        baseUrl.Should().Be("https://api.groq.com/openai/v1");
        model.Should().Be("llama-3-70b");
        source.Should().Be("user");
    }

    // --- URL Validation Tests ---

    [Theory]
    [InlineData("https://api.openai.com/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("https://api.groq.com/openai/v1", true)]
    [InlineData("", true)] // empty = reset to default
    [InlineData("ftp://evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not-a-url", false)]
    [InlineData("file:///etc/passwd", false)]
    public void IsValidBaseUrl_ValidatesCorrectly(string url, bool expected)
    {
        Andy.CodeIndex.Api.Controllers.SettingsController.IsValidBaseUrl(url).Should().Be(expected);
    }
}
