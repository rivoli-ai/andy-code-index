namespace Andy.CodeIndex.Application.DTOs;

public class FileFilterOverridesDto
{
    public List<string>? AdditionalSkipExtensions { get; set; }
    public List<string>? AdditionalSkipPatterns { get; set; }
    public List<string>? RemoveSkipExtensions { get; set; }
    public long? MaxFileSizeBytes { get; set; }
}
