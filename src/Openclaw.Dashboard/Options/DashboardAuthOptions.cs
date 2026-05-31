namespace Openclaw.Dashboard.Options;

public sealed class DashboardAuthOptions
{
    public const string SectionName = "DashboardAuth";

    public bool Enabled { get; set; } = true;

    public string? PasswordHash { get; set; }

    public string CookieName { get; set; } = ".OpenclawDashboard.Auth";

    public int SessionHours { get; set; } = 12;
}
