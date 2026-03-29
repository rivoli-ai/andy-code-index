using System.Text.Json;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class FileFilterService : IFileFilterService
{
    private readonly FileFilterOptions _options;

    public FileFilterService(IOptions<FileFilterOptions> options)
    {
        _options = options.Value;
    }

    public (bool Skip, string? Reason) ShouldSkip(string filePath, long fileSize, Repository? repo = null)
    {
        var (skipExtensions, skipPatterns, maxFileSize) = GetEffectiveConfig(repo);

        // Check file size
        if (fileSize > maxFileSize)
            return (true, $"File size {fileSize} exceeds max {maxFileSize} bytes");

        // Check extension
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) &&
            skipExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return (true, $"Extension '{ext}' is in skip list");

        // Check glob patterns
        foreach (var pattern in skipPatterns)
        {
            if (GitService.MatchGlob(filePath, pattern))
                return (true, $"Path matches skip pattern '{pattern}'");
        }

        return (false, null);
    }

    internal (List<string> SkipExtensions, List<string> SkipPatterns, long MaxFileSize) GetEffectiveConfig(Repository? repo)
    {
        var extensions = new List<string>(_options.SkipExtensions);
        var patterns = new List<string>(_options.SkipPatterns);
        var maxSize = _options.MaxFileSizeBytes;

        if (repo?.FileFilterOverrides is not null)
        {
            try
            {
                var overrides = JsonSerializer.Deserialize<FileFilterOverridesDto>(
                    repo.FileFilterOverrides,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (overrides is not null)
                {
                    if (overrides.AdditionalSkipExtensions is not null)
                        extensions.AddRange(overrides.AdditionalSkipExtensions);

                    if (overrides.AdditionalSkipPatterns is not null)
                        patterns.AddRange(overrides.AdditionalSkipPatterns);

                    if (overrides.RemoveSkipExtensions is not null)
                    {
                        extensions.RemoveAll(e =>
                            overrides.RemoveSkipExtensions.Contains(e, StringComparer.OrdinalIgnoreCase));
                    }

                    if (overrides.MaxFileSizeBytes.HasValue)
                        maxSize = overrides.MaxFileSizeBytes.Value;
                }
            }
            catch (JsonException)
            {
                // Invalid JSON — fall back to global config
            }
        }

        return (extensions, patterns, maxSize);
    }
}
