namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class DashboardSummary
{
    public int Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public DateTime SnapshotAt { get; set; }
}
