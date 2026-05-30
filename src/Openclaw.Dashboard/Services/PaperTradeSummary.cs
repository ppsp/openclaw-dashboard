namespace Openclaw.Dashboard.Services;

public sealed record PaperTradeSummary(
    string Portfolio,
    int OpenTrades,
    int ClosedTrades,
    decimal RealizedPnl,
    decimal? RealizedPnlPct,
    int LinkedSignals);
