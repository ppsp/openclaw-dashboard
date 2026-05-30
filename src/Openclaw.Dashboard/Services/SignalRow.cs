namespace Openclaw.Dashboard.Services;

public sealed record SignalRow(
    int Id,
    string Ticker,
    string Source,
    DateTime? DiscoveredAt,
    int? Tier1Score,
    int? Tier1Pass,
    string? OutcomeStatus,
    int? Rating);
