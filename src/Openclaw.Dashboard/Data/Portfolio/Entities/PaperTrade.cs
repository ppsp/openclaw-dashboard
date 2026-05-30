namespace Openclaw.Dashboard.Data.Portfolio.Entities;

public sealed class PaperTrade
{
    public int Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string EntryType { get; set; } = string.Empty;

    public decimal EntryPrice { get; set; }

    public decimal Quantity { get; set; }

    public string? ContractDetails { get; set; }

    public string EntryDate { get; set; } = string.Empty;

    public string Thesis { get; set; } = string.Empty;

    public decimal? TpPrice { get; set; }

    public decimal? SlPrice { get; set; }

    public int? InitialConfidence { get; set; }

    public string? Status { get; set; }

    public string? CloseDate { get; set; }

    public decimal? ClosePrice { get; set; }

    public string? CloseReason { get; set; }

    public decimal? RealizedPnl { get; set; }

    public string? CreatedAt { get; set; }

    public string? UpdatedAt { get; set; }

    public string Portfolio { get; set; } = string.Empty;

    public int? SignalId { get; set; }

    public string? Tier2Snapshot { get; set; }

    public string? EntryMonitorStatus { get; set; }

    public string? CancelledReason { get; set; }
}
