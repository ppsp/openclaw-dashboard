namespace Openclaw.Dashboard.Options;

public sealed class OpenclawPathsOptions
{
    public const string SectionName = "Openclaw";

    public string RootPath { get; set; } = string.Empty;

    public string WorkspacePath { get; set; } = string.Empty;

    public string CronPath { get; set; } = string.Empty;

    public string SqlitePath { get; set; } = string.Empty;
}
