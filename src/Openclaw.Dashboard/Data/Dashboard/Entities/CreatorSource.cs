namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class CreatorSource
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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
