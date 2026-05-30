namespace Openclaw.Dashboard.Services;

public sealed class CommandCenterSummary
{
    public int ActiveSignals { get; init; }

    public int Tier1Pass { get; init; }

    public int Tier2Complete { get; init; }

    public int OpenPaperTrades { get; init; }

    public int BrokenCrons { get; init; }

    public int TodaysSignals { get; init; }

    public DateTime AsOf { get; init; } = DateTime.Now;

    public string Source { get; init; } = "fallback";

    public IReadOnlyList<CommandCenterMetric> ToMetrics()
    {
        return
        [
            new("Active signals", ActiveSignals, "Signals not marked delivered or rejected", MudBlazor.Icons.Material.Filled.Radar, MudBlazor.Color.Primary),
            new("Tier 1 pass", Tier1Pass, "Signals passing first-stage classification", MudBlazor.Icons.Material.Filled.FilterAlt, MudBlazor.Color.Success),
            new("Tier 2 complete", Tier2Complete, "Signals with trade engineering output", MudBlazor.Icons.Material.Filled.TaskAlt, MudBlazor.Color.Info),
            new("Open paper trades", OpenPaperTrades, "Paper trades currently open", MudBlazor.Icons.Material.Filled.AssignmentTurnedIn, MudBlazor.Color.Secondary),
            new("Broken crons", BrokenCrons, "Cron jobs reporting errors or failed status", MudBlazor.Icons.Material.Filled.MonitorHeart, MudBlazor.Color.Error),
            new("Today's signals", TodaysSignals, "Signals discovered since local midnight", MudBlazor.Icons.Material.Filled.Today, MudBlazor.Color.Warning)
        ];
    }
}
