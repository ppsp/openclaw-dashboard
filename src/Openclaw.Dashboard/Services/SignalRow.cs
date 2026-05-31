namespace Openclaw.Dashboard.Services;

public sealed record SignalRow(
    int Id,
    string Ticker,
    string Direction,
    string Description,
    string DescriptionTooltip,
    string Source,
    string? Url,
    DateTime? DiscoveredAt,
    string Route,
    int? AlphaScore,
    int? ReadinessScore,
    string ReasonCategory,
    string NextAction,
    string? Status,
    int? Tier1Score,
    int? Tier1Pass,
    string? OutcomeStatus,
    int? Rating);
