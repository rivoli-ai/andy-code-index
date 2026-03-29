using Andy.CodeIndex.Application.DTOs;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IReportService
{
    Task<ReportDto> GenerateReportAsync(Guid repositoryId, CancellationToken ct = default);
    Task<string> ExportHtmlAsync(Guid repositoryId, CancellationToken ct = default);
}
