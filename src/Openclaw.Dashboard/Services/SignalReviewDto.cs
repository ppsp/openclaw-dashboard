namespace Openclaw.Dashboard.Services;

public sealed record SignalReviewDto(
    int SignalId,
    string Stage,
    string Decision,
    string? Note,
    string? Reviewer,
    DateTime? UpdatedAt);
