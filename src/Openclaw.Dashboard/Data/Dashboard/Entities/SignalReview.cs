namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class SignalReview
{
    public int Id { get; set; }

    public int SignalId { get; set; }

    public string Status { get; set; } = "open";

    public int? Rating { get; set; }

    public string? Note { get; set; }

    public string? Reviewer { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
