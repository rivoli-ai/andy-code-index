namespace Andy.CodeIndex.Domain.Entities;

public class SettingsChangeLog
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public required string Action { get; set; } // set, removed, updated
    public DateTime CreatedAt { get; set; }
}
