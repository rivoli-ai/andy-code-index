using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class CommitComparisonServiceTests
{
    [Fact]
    public void Compare_EmptyCommits_ReturnsEmptyResult()
    {
        var result = CommitComparisonService.Compare(
            "abc1234", "def5678",
            new List<Enrichment>(), new List<Enrichment>());

        result.From.Should().Be("abc1234");
        result.To.Should().Be("def5678");
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Compare_AddedEnrichments_DetectedCorrectly()
    {
        var fromEnrichments = new List<Enrichment>();
        var toEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/File1.cs", EnrichmentSubtype.Chunk, "content1"),
            MakeEnrichment("src/File2.cs", EnrichmentSubtype.APIDocs, "api doc content")
        };

        var result = CommitComparisonService.Compare("aaa", "bbb", fromEnrichments, toEnrichments);

        result.Added.Should().HaveCount(2);
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
        result.Added[0].FilePath.Should().Be("src/File1.cs");
        result.Added[1].FilePath.Should().Be("src/File2.cs");
    }

    [Fact]
    public void Compare_RemovedEnrichments_DetectedCorrectly()
    {
        var fromEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/OldFile.cs", EnrichmentSubtype.Chunk, "old content")
        };
        var toEnrichments = new List<Enrichment>();

        var result = CommitComparisonService.Compare("aaa", "bbb", fromEnrichments, toEnrichments);

        result.Added.Should().BeEmpty();
        result.Removed.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
        result.Removed[0].FilePath.Should().Be("src/OldFile.cs");
    }

    [Fact]
    public void Compare_ChangedEnrichments_DetectedCorrectly()
    {
        var fromEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/File.cs", EnrichmentSubtype.Chunk, "original content")
        };
        var toEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/File.cs", EnrichmentSubtype.Chunk, "modified content")
        };

        var result = CommitComparisonService.Compare("aaa", "bbb", fromEnrichments, toEnrichments);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().HaveCount(1);
        result.Changed[0].From.Content.Should().Be("original content");
        result.Changed[0].To.Content.Should().Be("modified content");
    }

    [Fact]
    public void Compare_UnchangedEnrichments_NotIncluded()
    {
        var fromEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/Stable.cs", EnrichmentSubtype.Chunk, "same content")
        };
        var toEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/Stable.cs", EnrichmentSubtype.Chunk, "same content")
        };

        var result = CommitComparisonService.Compare("aaa", "bbb", fromEnrichments, toEnrichments);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Compare_MixedChanges_CategorizesCorrectly()
    {
        var repoId = Guid.NewGuid();

        var fromEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/Kept.cs", EnrichmentSubtype.Chunk, "content v1"),
            MakeEnrichment("src/Removed.cs", EnrichmentSubtype.Chunk, "removed content"),
            MakeEnrichment("src/Unchanged.cs", EnrichmentSubtype.APIDocs, "api doc")
        };
        var toEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/Kept.cs", EnrichmentSubtype.Chunk, "content v2"),
            MakeEnrichment("src/New.cs", EnrichmentSubtype.Chunk, "new content"),
            MakeEnrichment("src/Unchanged.cs", EnrichmentSubtype.APIDocs, "api doc")
        };

        var result = CommitComparisonService.Compare("aaa", "bbb", fromEnrichments, toEnrichments);

        result.Added.Should().HaveCount(1);
        result.Added[0].FilePath.Should().Be("src/New.cs");
        result.Removed.Should().HaveCount(1);
        result.Removed[0].FilePath.Should().Be("src/Removed.cs");
        result.Changed.Should().HaveCount(1);
        result.Changed[0].From.FilePath.Should().Be("src/Kept.cs");
    }

    [Fact]
    public void Compare_SameFileDifferentSubtypes_TreatedAsSeparate()
    {
        var fromEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/File.cs", EnrichmentSubtype.Chunk, "chunk content")
        };
        var toEnrichments = new List<Enrichment>
        {
            MakeEnrichment("src/File.cs", EnrichmentSubtype.APIDocs, "api doc content")
        };

        var result = CommitComparisonService.Compare("aaa", "bbb", fromEnrichments, toEnrichments);

        // Chunk was removed, ApiDoc was added -- different subtypes
        result.Added.Should().HaveCount(1);
        result.Added[0].Subtype.Should().Be(EnrichmentSubtype.APIDocs);
        result.Removed.Should().HaveCount(1);
        result.Removed[0].Subtype.Should().Be(EnrichmentSubtype.Chunk);
        result.Changed.Should().BeEmpty();
    }

    private static Enrichment MakeEnrichment(string filePath, EnrichmentSubtype subtype, string content)
    {
        return new Enrichment
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            Type = EnrichmentType.Development,
            Subtype = subtype,
            Content = content,
            FilePath = filePath,
            CreatedAt = DateTime.UtcNow
        };
    }
}
