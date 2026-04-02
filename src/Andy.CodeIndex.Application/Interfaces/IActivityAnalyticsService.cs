using Andy.CodeIndex.Application.DTOs;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IActivityAnalyticsService
{
    Task<GitActivityHeatmapDto> GetHeatmapAsync(Guid repositoryId, int weeksBack = 52, CancellationToken ct = default);
    Task<SparklineDto> GetSparklineAsync(Guid repositoryId, int weeksBack = 52, CancellationToken ct = default);
    Task<Dictionary<Guid, SparklineDto>> GetBulkSparklinesAsync(IEnumerable<Guid> repositoryIds, int weeksBack = 52, CancellationToken ct = default);
}
