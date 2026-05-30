namespace Openclaw.Dashboard.Services;

public sealed record AppSettingDefinition(
    string Key,
    string Category,
    string Label,
    string Description,
    AppSettingValueType ValueType,
    string DefaultValue,
    bool IsDangerous = false,
    string? DangerousMessage = null,
    decimal? Min = null,
    decimal? Max = null,
    string? Suffix = null,
    IReadOnlyList<string>? Options = null);
