namespace Openclaw.Dashboard.Services;

public sealed record SignalReviewRequest(
    int SignalId,
    string Stage,
    string Decision,
    string? Note,
    string Reviewer,
    string AdminToken);
