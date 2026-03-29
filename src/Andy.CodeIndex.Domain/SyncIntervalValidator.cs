namespace Andy.CodeIndex.Domain;

public static class SyncIntervalValidator
{
    /// <summary>
    /// Allowed values for SyncIntervalMinutes.
    /// null = use system default, 0 = manual only,
    /// 15/30/60/120/360/720/1440 = scheduled intervals.
    /// </summary>
    public static readonly int[] AllowedValues = [0, 15, 30, 60, 120, 360, 720, 1440];

    /// <summary>
    /// Returns true if the value is valid: null (default) or one of the allowed integers.
    /// </summary>
    public static bool IsValid(int? value)
    {
        if (value is null)
            return true;

        return Array.Exists(AllowedValues, v => v == value.Value);
    }
}
