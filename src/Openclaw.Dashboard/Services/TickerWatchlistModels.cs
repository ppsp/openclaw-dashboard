namespace Openclaw.Dashboard.Services;

public sealed class TickerWatchlistEditModel
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
}

public sealed record TickerWatchlistRow(
    int Id,
    string Symbol,
    string AssetClass,
    string? Sector,
    string? Description,
    string? WatchReason,
    string Status,
    int? Conviction,
    string? TimeHorizon,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);
