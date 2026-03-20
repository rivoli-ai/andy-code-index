using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class RankFusionServiceTests
{
    private readonly RankFusionService _service = new();

    [Fact]
    public void Fuse_SingleList_ReturnsRankedByRRF()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var inputs = new List<RankedResultSet>
        {
            new()
            {
                Source = "semantic",
                Results =
                [
                    new FusedResult { EnrichmentId = id1, Content = "first", OriginalScore = 0.9 },
                    new FusedResult { EnrichmentId = id2, Content = "second", OriginalScore = 0.7 }
                ]
            }
        };

        var result = _service.Fuse(inputs);

        result.Should().HaveCount(2);
        result[0].EnrichmentId.Should().Be(id1);
        result[0].FusedScore.Should().BeApproximately(1.0 / 60, 0.001); // rank 0: 1/(60+0)
        result[1].FusedScore.Should().BeApproximately(1.0 / 61, 0.001); // rank 1: 1/(60+1)
    }

    [Fact]
    public void Fuse_OverlappingResults_SumsScores()
    {
        var sharedId = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();

        var inputs = new List<RankedResultSet>
        {
            new()
            {
                Source = "semantic",
                Results = [new FusedResult { EnrichmentId = sharedId, Content = "shared" }]
            },
            new()
            {
                Source = "bm25",
                Results = [
                    new FusedResult { EnrichmentId = sharedId, Content = "shared" },
                    new FusedResult { EnrichmentId = uniqueId, Content = "unique" }
                ]
            }
        };

        var result = _service.Fuse(inputs);

        result.Should().HaveCount(2);
        // Shared item appears in both lists at rank 0 → score = 2 * (1/60)
        var shared = result.First(r => r.EnrichmentId == sharedId);
        shared.FusedScore.Should().BeApproximately(2.0 / 60, 0.001);

        // Unique item only in bm25 at rank 1 → score = 1/61
        var unique = result.First(r => r.EnrichmentId == uniqueId);
        unique.FusedScore.Should().BeApproximately(1.0 / 61, 0.001);

        // Shared should rank higher
        result[0].EnrichmentId.Should().Be(sharedId);
    }

    [Fact]
    public void Fuse_NonOverlapping_MergesAll()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var inputs = new List<RankedResultSet>
        {
            new() { Source = "semantic", Results = [new FusedResult { EnrichmentId = id1, Content = "a" }] },
            new() { Source = "bm25", Results = [new FusedResult { EnrichmentId = id2, Content = "b" }] }
        };

        var result = _service.Fuse(inputs);

        result.Should().HaveCount(2);
        // Both at rank 0 in their respective lists, equal RRF scores
        result[0].FusedScore.Should().BeApproximately(result[1].FusedScore, 0.001);
    }

    [Fact]
    public void Fuse_EmptyInputs_ReturnsEmpty()
    {
        _service.Fuse([]).Should().BeEmpty();
    }

    [Fact]
    public void Fuse_EmptyResultSets_ReturnsEmpty()
    {
        var inputs = new List<RankedResultSet>
        {
            new() { Source = "semantic", Results = [] },
            new() { Source = "bm25", Results = [] }
        };

        _service.Fuse(inputs).Should().BeEmpty();
    }

    [Fact]
    public void Fuse_CustomK_AffectsScoring()
    {
        var id = Guid.NewGuid();

        var inputs = new List<RankedResultSet>
        {
            new() { Source = "semantic", Results = [new FusedResult { EnrichmentId = id, Content = "a" }] }
        };

        var resultK60 = _service.Fuse(inputs, k: 60);
        var resultK10 = _service.Fuse(inputs, k: 10);

        // Lower k = higher RRF score for top-ranked items
        resultK10[0].FusedScore.Should().BeGreaterThan(resultK60[0].FusedScore);
    }
}
