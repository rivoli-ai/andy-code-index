namespace Andy.CodeIndex.Application.Interfaces;

public interface IBinaryDetectionService
{
    (bool IsBinary, string? Reason) IsBinary(string filePath);
}
