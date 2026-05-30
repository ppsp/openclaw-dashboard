namespace Openclaw.Dashboard.Services;

public sealed record SignalReviewRequest(
    int SignalId,
    string Decision,
    int Rating,
    string? Note,
    string Reviewer,
    string AdminToken);
