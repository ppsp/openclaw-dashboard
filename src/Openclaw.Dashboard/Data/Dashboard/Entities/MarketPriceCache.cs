namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class MarketPriceCache
{
    public string Symbol { get; set; } = string.Empty;

    public string Provider { get; set; } = "Yahoo";

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public decimal? PreviousClose { get; set; }

    public decimal? Change { get; set; }

    public decimal? ChangePct { get; set; }

    public DateTime? QuoteTimeUtc { get; set; }

    public DateTime? FetchedAtUtc { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    public string? LastError { get; set; }
}
