namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class CreatorEvaluation
{
    public int Id { get; set; }

    public int CreatorSourceId { get; set; }

    public CreatorSource? CreatorSource { get; set; }

    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PeriodStart { get; set; }

    public DateTime? PeriodEnd { get; set; }

    public int SignalsCount { get; set; }

    public int GoodCount { get; set; }

    public int MediumCount { get; set; }

    public int BadCount { get; set; }

    public int PassCount { get; set; }

    public int WatchCount { get; set; }

    public int RejectCount { get; set; }

    public double? AverageAlphaScore { get; set; }

    public double? AverageReadinessScore { get; set; }

    public double Score { get; set; }

    public string? Summary { get; set; }
}
