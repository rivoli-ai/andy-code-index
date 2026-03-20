using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;

namespace Andy.CodeIndex.Application.Interfaces;

public interface ITaskHandler
{
    TaskOperation Operation { get; }
    Task HandleAsync(IndexingTask task, CancellationToken ct = default);
}
