namespace Openclaw.Dashboard.Data.Signals.Entities;

public sealed class Signal
{
    public int Id { get; set; }

    public string RawSignal { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? Url { get; set; }

    public DateTime? DiscoveredAt { get; set; }

    public string? Status { get; set; }

    public string? Fingerprint { get; set; }

    public string? Sources { get; set; }

    public int? Tier1Score { get; set; }

    public string? Tier1Dims { get; set; }

    public int? Tier1Pass { get; set; }

    public string? Tier1Route { get; set; }

    public int? AlphaScore { get; set; }

    public int? ReadinessScore { get; set; }

    public string? ReasonCategory { get; set; }

    public string? NextAction { get; set; }

    public string? Tier2Result { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public int? Rating { get; set; }

    public string? OutcomeStatus { get; set; }

    public DateTime? TriggeredAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? OutcomeNote { get; set; }

    public DateTime? MonitoringStart { get; set; }

    public int? TtlDays { get; set; }

    public string? UpdatedAt { get; set; }
}
