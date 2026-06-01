namespace Openclaw.Dashboard.Data.Portfolio.Entities;

public sealed class TradeCheckup
{
    public int Id { get; set; }

    public int? TradeId { get; set; }

    public string CheckupDate { get; set; } = string.Empty;

    public decimal? CurrentPrice { get; set; }

    public decimal? UnrealizedPnl { get; set; }

    public decimal? PnlPct { get; set; }

    public int? DaysHeld { get; set; }

    public int? ThesisStillValid { get; set; }

    public int? ConfidenceCurrent { get; set; }

    public string? Recommendation { get; set; }

    public string? Notes { get; set; }
}
