namespace Openclaw.Dashboard.Services;

public sealed class CommandCenterSummary
{
    public int ActiveSignals { get; init; }

    public int Tier1Pass { get; init; }

    public int NewUnrouted { get; init; }

    public int Watch { get; init; }

    public int PassPendingTier2 { get; init; }

    public int FastTrackPendingTier2 { get; init; }

    public int Rejected { get; init; }

    public int Tier2Complete { get; init; }

    public int ActiveTriggered { get; init; }

    public int StaleWatch { get; init; }

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
            new("New / unrouted", NewUnrouted, "New signals without an authoritative Tier 1 route", MudBlazor.Icons.Material.Filled.NewReleases, MudBlazor.Color.Default),
            new("Watch queue", Watch, "Signals waiting on confirmation, pullback, or recheck", MudBlazor.Icons.Material.Filled.Visibility, MudBlazor.Color.Info),
            new("Pass pending T2", PassPendingTier2, "Routed pass and waiting for Tier 2", MudBlazor.Icons.Material.Filled.FilterAlt, MudBlazor.Color.Success),
            new("Fast track T2", FastTrackPendingTier2, "Fast-track signals still awaiting Tier 2 risk checks", MudBlazor.Icons.Material.Filled.Bolt, MudBlazor.Color.Warning),
            new("Rejected", Rejected, "Signals routed reject or marked Tier 1 reject", MudBlazor.Icons.Material.Filled.Block, MudBlazor.Color.Error),
            new("Tier 2 complete", Tier2Complete, "Signals with trade engineering output", MudBlazor.Icons.Material.Filled.TaskAlt, MudBlazor.Color.Info),
            new("Active / triggered", ActiveTriggered, "Signals currently active or triggered", MudBlazor.Icons.Material.Filled.TrendingUp, MudBlazor.Color.Primary),
            new("Stale watch", StaleWatch, "Watch signals past discovered time plus TTL", MudBlazor.Icons.Material.Filled.TimerOff, MudBlazor.Color.Warning),
            new("Open paper trades", OpenPaperTrades, "Paper trades currently open", MudBlazor.Icons.Material.Filled.AssignmentTurnedIn, MudBlazor.Color.Secondary),
            new("Broken crons", BrokenCrons, "Cron jobs reporting errors or failed status", MudBlazor.Icons.Material.Filled.MonitorHeart, MudBlazor.Color.Error),
            new("Today's signals", TodaysSignals, "Signals discovered since local midnight", MudBlazor.Icons.Material.Filled.Today, MudBlazor.Color.Warning)
        ];
    }
}
