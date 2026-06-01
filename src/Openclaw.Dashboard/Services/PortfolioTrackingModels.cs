namespace Openclaw.Dashboard.Services;

public sealed record PortfolioDashboard(
    IReadOnlyList<PortfolioAccountSummary> Accounts,
    IReadOnlyList<PortfolioHoldingRow> Holdings,
    IReadOnlyList<PortfolioTransactionRow> Transactions,
    IReadOnlyList<PortfolioWatchItem> WatchItems,
    IReadOnlyList<PortfolioChartPoint> AccountAllocation,
    IReadOnlyList<PortfolioChartPoint> HoldingAllocation,
    IReadOnlyList<PortfolioMonthlyActivity> MonthlyActivity,
    DateTime? LatestUpdate,
    decimal? TotalCurrentValue,
    decimal? TotalUnrealizedPnl,
    decimal? TotalUnrealizedPnlPct,
    string? TotalCurrentValueCurrency,
    DateTime? LatestPriceFetchedAt);

public sealed record PortfolioAccountSummary(
    string AccountId,
    string AccountName,
    int HoldingsCount,
    decimal? BookValue,
    int UnknownValueHoldings,
    decimal? CashLikeValue,
    decimal? CashLikePct,
    decimal? CurrentValue,
    decimal? UnrealizedPnl,
    decimal? UnrealizedPnlPct,
    string? CurrentValueCurrency,
    DateTime? LatestUpdate,
    DateTime? LatestPriceFetchedAt,
    bool HasData);

public sealed record PortfolioHoldingRow(
    string AccountId,
    string AccountName,
    string Ticker,
    decimal Quantity,
    decimal? AvgPriceCad,
    decimal? AvgPriceUsd,
    decimal? BookValue,
    string BookValueCurrency,
    decimal? AccountWeightPct,
    DateTime? LastUpdate,
    bool MissingCostBasis,
    bool IsCashLike,
    decimal? CurrentPrice,
    string? CurrentPriceCurrency,
    DateTime? CurrentPriceFetchedAt,
    string? CurrentPriceProvider,
    string? CurrentPriceError,
    decimal? CurrentValue,
    decimal? UnrealizedPnl,
    decimal? UnrealizedPnlPct);

public sealed record PortfolioTransactionRow(
    int Id,
    string AccountId,
    string AccountName,
    DateTime? Date,
    string Ticker,
    decimal Amount,
    decimal? PriceCad,
    decimal? PriceUsd,
    string Activity,
    string? Comment);

public sealed record PortfolioWatchItem(
    string Severity,
    string Title,
    string Detail,
    string? AccountName,
    string? Ticker);

public sealed record PortfolioChartPoint(string Label, double Value);

public sealed record PortfolioMonthlyActivity(string Month, double BuyValue, double SellValue);
