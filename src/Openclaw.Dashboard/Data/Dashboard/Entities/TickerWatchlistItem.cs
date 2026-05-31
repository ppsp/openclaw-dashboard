namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class TickerWatchlistItem
{
    public int Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string AssetClass { get; set; } = "stock";

    public string? Sector { get; set; }

    public string? Description { get; set; }

    public string? WatchReason { get; set; }

    public string Status { get; set; } = "active";

    public int? Conviction { get; set; }

    public string? TimeHorizon { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
