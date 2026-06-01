namespace Openclaw.Dashboard.Services;

public sealed record PaperTradeHistoryRow(
    int Id,
    int? TradeId,
    string Ticker,
    string Portfolio,
    string Status,
    string CheckupDate,
    decimal? CurrentPrice,
    decimal? UnrealizedPnl,
    decimal? PnlPct,
    int? DaysHeld,
    int? ThesisStillValid,
    int? ConfidenceCurrent,
    string? Recommendation,
    string? Notes);
