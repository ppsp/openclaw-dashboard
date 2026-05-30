namespace Openclaw.Dashboard.Services;

public sealed record PaperTradeRow(
    int Id,
    string Ticker,
    string Direction,
    decimal EntryPrice,
    decimal Quantity,
    decimal? CurrentOrClosePrice,
    decimal? TpPrice,
    decimal? SlPrice,
    string Status,
    decimal? RealizedPnl,
    decimal? RealizedPnlPct,
    string Portfolio,
    int? SignalId,
    string EntryType,
    string EntryDate);
