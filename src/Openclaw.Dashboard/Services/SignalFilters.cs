namespace Openclaw.Dashboard.Services;

public sealed class SignalFilters
{
    public string? Source { get; init; }

    public string? OutcomeStatus { get; init; }

    public string? RouteView { get; init; }

    public DateTime? FromDate { get; init; }

    public DateTime? ToDate { get; init; }

    public string? Ticker { get; init; }
}
