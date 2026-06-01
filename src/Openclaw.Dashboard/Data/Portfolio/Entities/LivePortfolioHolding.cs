namespace Openclaw.Dashboard.Data.Portfolio.Entities;

public sealed class LivePortfolioHolding
{
    public string Ticker { get; set; } = string.Empty;

    public decimal NetAmount { get; set; }

    public string AccountId { get; set; } = string.Empty;

    public decimal? AvgPriceCad { get; set; }

    public string? LastUpdate { get; set; }

    public decimal? AvgPriceUsd { get; set; }
}
