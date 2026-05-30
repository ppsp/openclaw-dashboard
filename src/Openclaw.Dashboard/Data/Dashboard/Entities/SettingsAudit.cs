namespace Openclaw.Dashboard.Data.Dashboard.Entities;

public sealed class SettingsAudit
{
    public int Id { get; set; }

    public string SettingKey { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string Actor { get; set; } = "dashboard";

    public DateTime CreatedAt { get; set; }
}
