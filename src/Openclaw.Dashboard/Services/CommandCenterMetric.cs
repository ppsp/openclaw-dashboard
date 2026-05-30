namespace Openclaw.Dashboard.Services;

public sealed record CommandCenterMetric(
    string Label,
    int Value,
    string Detail,
    string Icon,
    MudBlazor.Color Color);
