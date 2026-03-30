using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class SyncRepositoryHandlerTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly SyncRepositoryHandler _handler;
    private readonly Repository _testRepo;

    public SyncRepositoryHandlerTests()
    {
        _context = TestDbContextFactory.Create();
        _handler = new SyncRepositoryHandler(
            _context, _gitServiceMock.Object,
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            NullLogger<SyncRepositoryHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsSyncRepository()
    {
        _handler.Operation.Should().Be(TaskOperation.SyncRepository);
    }

    [Fact]
    public async Task HandleAsync_FetchesAndUpdatesTimestamp()
    {
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns("/tmp/test/repos/x");
        _gitServiceMock.Setup(g => g.FetchAsync("/tmp/test/repos/x", null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.SyncRepository, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var repo = await _context.Repositories.FindAsync(_testRepo.Id);
        repo!.LastSyncedAt.Should().NotBeNull();
        _gitServiceMock.Verify(g => g.FetchAsync("/tmp/test/repos/x", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class CreateBM25IndexHandlerTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly CreateBM25IndexHandler _handler;
    private readonly Repository _testRepo;

    public CreateBM25IndexHandlerTests()
    {
        _context = TestDbContextFactory.Create();
        _handler = new CreateBM25IndexHandler(_context, NullLogger<CreateBM25IndexHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsCreateBM25Index()
    {
        _handler.Operation.Should().Be(TaskOperation.CreateBM25Index);
    }

    [Fact]
    public async Task HandleAsync_CompletesWithoutError()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateBM25Index, CreatedAt = DateTime.UtcNow
        };

        var act = () => _handler.HandleAsync(task);
        await act.Should().NotThrowAsync();
    }
}

public class CreateCodeEmbeddingsHandlerTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly CreateCodeEmbeddingsHandler _handler;
    private readonly Repository _testRepo;

    private readonly Mock<IApiKeyResolver> _resolverMock = new();

    public CreateCodeEmbeddingsHandlerTests()
    {
        _context = TestDbContextFactory.Create();
        _embeddingServiceMock.Setup(e => e.IsAvailable).Returns(false);
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((string?)null, "https://api.openai.com/v1", "text-embedding-3-small", "none"));

        _handler = new CreateCodeEmbeddingsHandler(
            _context, _embeddingServiceMock.Object, _resolverMock.Object,
            Options.Create(new EmbeddingOptions()),
            NullLogger<CreateCodeEmbeddingsHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsCreateCodeEmbeddings()
    {
        _handler.Operation.Should().Be(TaskOperation.CreateCodeEmbeddings);
    }

    [Fact]
    public async Task HandleAsync_SkipsWhenNotAvailable()
    {
        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateCodeEmbeddings, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        // Should not attempt to generate embeddings
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingsAsync(
            It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GeneratesWhenAvailable()
    {
        _embeddingServiceMock.Setup(e => e.IsAvailable).Returns(true);
        _resolverMock.Setup(r => r.ResolveEmbeddingKeyAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sk-test-key", "https://api.openai.com/v1", "text-embedding-3-small", "user"));

        // Add a chunk enrichment
        _context.Enrichments.Add(new Enrichment
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Type = EnrichmentType.Development, Subtype = EnrichmentSubtype.Chunk,
            Content = "test code", CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new float[] { 0.1f, 0.2f } });
        _embeddingServiceMock.Setup(e => e.StoreEmbeddingsAsync(
            It.IsAny<Guid[]>(), It.IsAny<float[][]>(), IndexType.Code, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id,
            Operation = TaskOperation.CreateCodeEmbeddings, CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingsAsync(
            It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
