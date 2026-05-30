namespace Openclaw.Dashboard.Services;

public sealed record SignalReviewDto(
    int SignalId,
    string Decision,
    int? Rating,
    string? Note,
    string? Reviewer,
    DateTime? UpdatedAt);
