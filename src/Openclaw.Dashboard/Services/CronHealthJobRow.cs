namespace Openclaw.Dashboard.Services;

public sealed class CronHealthJobRow
{
    public string JobId { get; init; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool? Enabled { get; set; }

    public string Schedule { get; set; } = string.Empty;

    public DateTime? LastRun { get; set; }

    public DateTime? NextRun { get; set; }

    public string Status { get; set; } = "unknown";

    public long? DurationMs { get; set; }

    public string? Error { get; set; }

    public string Source { get; set; } = string.Empty;

    public string DurationText => DurationMs is null
        ? "unknown"
        : TimeSpan.FromMilliseconds(DurationMs.Value).TotalSeconds < 60
            ? $"{TimeSpan.FromMilliseconds(DurationMs.Value).TotalSeconds:N1}s"
            : $"{TimeSpan.FromMilliseconds(DurationMs.Value):m\\:ss}";
}
