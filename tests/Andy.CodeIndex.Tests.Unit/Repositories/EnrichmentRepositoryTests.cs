using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Repositories;

public class EnrichmentRepositoryTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly EnrichmentRepository _repo;
    private readonly Repository _testRepo;

    public EnrichmentRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repo = new EnrichmentRepository(_context);
        _testRepo = new Repository
        {
            Id = Guid.NewGuid(), Name = "test", Url = "https://github.com/t/r",
            Provider = GitProvider.GitHub, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private Enrichment CreateEnrichment(
        EnrichmentType type = EnrichmentType.Development,
        EnrichmentSubtype subtype = EnrichmentSubtype.Chunk,
        string? language = null, string? filePath = null)
    {
        return new Enrichment
        {
            Id = Guid.NewGuid(), RepositoryId = _testRepo.Id, Type = type, Subtype = subtype,
            Content = "test content", Language = language, FilePath = filePath,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task QueryAsync_NoFilters_ReturnsAll()
    {
        await _repo.AddAsync(CreateEnrichment());
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Usage, subtype: EnrichmentSubtype.Cookbook));
        await _repo.SaveChangesAsync();

        var result = await _repo.QueryAsync();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAsync_FilterByType_ReturnsMatching()
    {
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Usage, subtype: EnrichmentSubtype.Cookbook));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development));
        await _repo.SaveChangesAsync();

        var result = await _repo.QueryAsync(type: EnrichmentType.Development);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAsync_FilterByLanguage_ReturnsMatching()
    {
        await _repo.AddAsync(CreateEnrichment(language: "csharp"));
        await _repo.AddAsync(CreateEnrichment(language: "python"));
        await _repo.AddAsync(CreateEnrichment(language: "csharp"));
        await _repo.SaveChangesAsync();

        var result = await _repo.QueryAsync(language: "csharp");
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAsync_CombinedFilters_NarrowsResults()
    {
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development, language: "csharp"));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development, language: "python"));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Usage, subtype: EnrichmentSubtype.Cookbook, language: "csharp"));
        await _repo.SaveChangesAsync();

        var result = await _repo.QueryAsync(type: EnrichmentType.Development, language: "csharp");
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task QueryAsync_Pagination_RespectsOffsetAndLimit()
    {
        for (int i = 0; i < 10; i++)
            await _repo.AddAsync(CreateEnrichment());
        await _repo.SaveChangesAsync();

        var result = await _repo.QueryAsync(offset: 3, limit: 4);
        result.Should().HaveCount(4);
    }

    [Fact]
    public async Task QueryCountAsync_ReturnsCorrectCount()
    {
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Usage, subtype: EnrichmentSubtype.Cookbook));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development));
        await _repo.SaveChangesAsync();

        var count = await _repo.QueryCountAsync(type: EnrichmentType.Development);
        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteByRepositoryAndTypeAsync_RemovesMatching()
    {
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Development));
        await _repo.AddAsync(CreateEnrichment(type: EnrichmentType.Usage, subtype: EnrichmentSubtype.Cookbook));
        await _repo.SaveChangesAsync();

        await _repo.DeleteByRepositoryAndTypeAsync(_testRepo.Id, EnrichmentType.Development);

        var remaining = await _repo.GetAllAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Type.Should().Be(EnrichmentType.Usage);
    }

    [Fact]
    public async Task GetByRepositoryAndSubtypeAsync_ReturnsMatching()
    {
        await _repo.AddAsync(CreateEnrichment(subtype: EnrichmentSubtype.Chunk));
        await _repo.AddAsync(CreateEnrichment(subtype: EnrichmentSubtype.APIDocs));
        await _repo.AddAsync(CreateEnrichment(subtype: EnrichmentSubtype.Chunk));
        await _repo.SaveChangesAsync();

        var result = await _repo.GetByRepositoryAndSubtypeAsync(_testRepo.Id, EnrichmentSubtype.Chunk);
        result.Should().HaveCount(2);
    }
}
