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

    private ApiKeyResolver CreateResolver(string? systemKey = null)
    {
        var options = Options.Create(new EmbeddingOptions { ApiKey = systemKey });
        return new ApiKeyResolver(_context, _encryptionMock.Object, options, NullLogger<ApiKeyResolver>.Instance);
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
}
