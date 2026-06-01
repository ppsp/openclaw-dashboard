namespace Openclaw.Dashboard.Data.Portfolio.Entities;

public sealed class PortfolioTransaction
{
    public int Id { get; set; }

    public string? InsertDate { get; set; }

    public string Ticker { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal? PriceCad { get; set; }

    public decimal? PriceUsd { get; set; }

    public string AccountId { get; set; } = string.Empty;

    public string? Comment { get; set; }
}
