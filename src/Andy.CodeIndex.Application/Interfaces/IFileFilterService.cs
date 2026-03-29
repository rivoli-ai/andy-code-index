using Andy.CodeIndex.Domain.Entities;

namespace Andy.CodeIndex.Application.Interfaces;

public interface IFileFilterService
{
    (bool Skip, string? Reason) ShouldSkip(string filePath, long fileSize, Repository? repo = null);
}
