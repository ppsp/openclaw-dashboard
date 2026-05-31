namespace Openclaw.Dashboard.Services;

public sealed record SignalRow(
    int Id,
    string Ticker,
    string Description,
    string DescriptionTooltip,
    string Source,
    string? Url,
    DateTime? DiscoveredAt,
    int? Tier1Score,
    int? Tier1Pass,
    string? OutcomeStatus,
    int? Rating);
