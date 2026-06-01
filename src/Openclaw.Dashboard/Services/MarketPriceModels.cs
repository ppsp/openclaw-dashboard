namespace Openclaw.Dashboard.Services;

public sealed record MarketPriceQuote(
    string Symbol,
    string Provider,
    decimal? Price,
    string? Currency,
    decimal? PreviousClose,
    decimal? Change,
    decimal? ChangePct,
    DateTime? QuoteTimeUtc,
    DateTime? FetchedAtUtc,
    DateTime? LastAttemptAtUtc,
    string? LastError);

public sealed record MarketPriceRefreshResult(
    int Requested,
    int Refreshed,
    int Failed,
    DateTime RefreshedAtUtc);
