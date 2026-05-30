namespace Openclaw.Dashboard.Services;

public sealed record SignalDetailDto(
    int Id,
    string Ticker,
    string Source,
    string? Url,
    DateTime? DiscoveredAt,
    string? Status,
    int? Tier1Score,
    int? Tier1Pass,
    int? Rating,
    string? OutcomeStatus,
    DateTime? TriggeredAt,
    DateTime? ResolvedAt,
    string? OutcomeNote,
    DateTime? MonitoringStart,
    int? TtlDays,
    string RawSignal,
    string Tier1DimsJson,
    string Tier2ResultJson);
