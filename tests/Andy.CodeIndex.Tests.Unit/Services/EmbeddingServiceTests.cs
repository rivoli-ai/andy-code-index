using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class EmbeddingServiceTests : IDisposable
{
    private readonly Mock<IEmbeddingProvider> _providerMock = new();
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly EmbeddingService _service;

    public EmbeddingServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _providerMock.Setup(p => p.Dimensions).Returns(1536);
        _service = new EmbeddingService(
            _providerMock.Object,
            _context,
            Options.Create(new EmbeddingOptions { MaxBatchSize = 3, MaxBatchChars = 100, Parallelism = 2 }),
            NullLogger<EmbeddingService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GenerateEmbeddingsAsync_EmptyInput_ReturnsEmpty()
    {
        var result = await _service.GenerateEmbeddingsAsync([]);
        result.Should().BeEmpty();
        _providerMock.Verify(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SingleBatch_CallsProviderOnce()
    {
        var texts = new[] { "hello", "world" };
        _providerMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new float[] { 1, 2, 3 }, new float[] { 4, 5, 6 } });

        var result = await _service.GenerateEmbeddingsAsync(texts);

        result.Should().HaveCount(2);
        _providerMock.Verify(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ExceedsBatchSize_SplitsIntoBatches()
    {
        // MaxBatchSize = 3, so 5 texts should produce 2 batches
        var texts = new[] { "a", "b", "c", "d", "e" };
        _providerMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] input, CancellationToken _) =>
                input.Select(_ => new float[] { 1, 2, 3 }).ToArray());

        var result = await _service.GenerateEmbeddingsAsync(texts);

        result.Should().HaveCount(5);
        _providerMock.Verify(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ExceedsBatchChars_SplitsIntoBatches()
    {
        // MaxBatchChars = 100, each text is 60 chars -> 2 batches
        var texts = new[] { new string('a', 60), new string('b', 60) };
        _providerMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] input, CancellationToken _) =>
                input.Select(_ => new float[] { 1 }).ToArray());

        var result = await _service.GenerateEmbeddingsAsync(texts);

        result.Should().HaveCount(2);
        _providerMock.Verify(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void CreateBatches_RespectsMaxBatchSize()
    {
        var texts = Enumerable.Range(0, 10).Select(i => $"text{i}").ToArray();
        var batches = _service.CreateBatches(texts);

        batches.Should().HaveCount(4); // 3+3+3+1
        batches[0].Should().HaveCount(3);
        batches[^1].Should().HaveCount(1);
    }

    [Fact]
    public void CreateBatches_RespectsMaxBatchChars()
    {
        // MaxBatchChars = 100
        var texts = new[] { new string('a', 40), new string('b', 40), new string('c', 40) };
        var batches = _service.CreateBatches(texts);

        batches.Should().HaveCount(2); // 40+40=80 fits, then 40 alone
    }

    [Fact]
    public void CreateBatches_EmptyInput_ReturnsEmpty()
    {
        _service.CreateBatches([]).Should().BeEmpty();
    }

    [Fact]
    public void CreateBatches_SingleLargeItem_PutsInOwnBatch()
    {
        var texts = new[] { new string('a', 200) }; // exceeds MaxBatchChars alone
        var batches = _service.CreateBatches(texts);

        batches.Should().HaveCount(1);
        batches[0].Should().HaveCount(1);
    }

    [Fact]
    public void Dimensions_ReturnsProviderDimensions()
    {
        _service.Dimensions.Should().Be(1536);
    }
}
