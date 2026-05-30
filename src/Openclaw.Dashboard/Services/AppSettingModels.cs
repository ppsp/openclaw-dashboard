namespace Openclaw.Dashboard.Services;

public sealed record AppSettingCategory(
    string Name,
    string Icon,
    IReadOnlyList<AppSettingValue> Settings);

public sealed record AppSettingValue(
    AppSettingDefinition Definition,
    string Value,
    DateTime UpdatedAt);
