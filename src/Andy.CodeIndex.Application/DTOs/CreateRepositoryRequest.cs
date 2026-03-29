using System.ComponentModel.DataAnnotations;

namespace Andy.CodeIndex.Application.DTOs;

public class CreateRepositoryRequest
{
    [Required]
    [Url]
    public required string Url { get; set; }

    public string? PersonalAccessToken { get; set; }

    public int? SyncIntervalMinutes { get; set; }
}

public class UpdateRepositoryRequest
{
    public int? SyncIntervalMinutes { get; set; }
    public FileFilterOverridesDto? FileFilterOverrides { get; set; }
}
