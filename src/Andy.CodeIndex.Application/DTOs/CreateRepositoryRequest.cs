using System.ComponentModel.DataAnnotations;

namespace Andy.CodeIndex.Application.DTOs;

public class CreateRepositoryRequest
{
    [Required]
    [Url]
    public required string Url { get; set; }

    public string? PersonalAccessToken { get; set; }
}
