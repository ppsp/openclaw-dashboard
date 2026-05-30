namespace Openclaw.Dashboard.Services;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalItems);
