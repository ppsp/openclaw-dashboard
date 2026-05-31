namespace Openclaw.Dashboard.Services;

public sealed class CreatorSourceEditModel
{
    public int Id { get; set; }

    public string Platform { get; set; } = "x";

    public string DisplayName { get; set; } = string.Empty;

    public string? Handle { get; set; }

    public string? ExternalId { get; set; }

    public string? Url { get; set; }

    public string Status { get; set; } = "active";

    public string TrustLevel { get; set; } = "normal";

    public bool ScoutEnabled { get; set; } = true;

    public string? Notes { get; set; }
}

public sealed record CreatorSourceRow(
    int Id,
    string Platform,
    string DisplayName,
    string? Handle,
    string? ExternalId,
    string? Url,
    string Status,
    string TrustLevel,
    bool ScoutEnabled,
    string? Notes,
    int SignalsCount,
    int GoodCount,
    int MediumCount,
    int BadCount,
    int PassCount,
    int WatchCount,
    int RejectCount,
    double? AverageAlphaScore,
    double? AverageReadinessScore,
    double? LatestScore,
    DateTime? LatestEvaluatedAt);

public sealed record CreatorEvaluationRow(
    int Id,
    int CreatorSourceId,
    DateTime EvaluatedAt,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    int SignalsCount,
    int GoodCount,
    int MediumCount,
    int BadCount,
    int PassCount,
    int WatchCount,
    int RejectCount,
    double? AverageAlphaScore,
    double? AverageReadinessScore,
    double Score,
    string? Summary);
