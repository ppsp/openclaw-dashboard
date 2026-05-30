namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class CronRun
{
    public int Id { get; set; }

    public string CronJobId { get; set; } = string.Empty;

    public string JobName { get; set; } = string.Empty;

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public string Status { get; set; } = "unknown";

    public long? DurationMs { get; set; }

    public string? SourceRunFile { get; set; }

    public string? Summary { get; set; }

    public string? Error { get; set; }
}
