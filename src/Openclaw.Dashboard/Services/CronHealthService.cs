using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class CronHealthService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    IOptions<OpenclawPathsOptions> openclawPaths,
    ILogger<CronHealthService> logger)
{
    private const int RunLimit = 500;

    public async Task<IReadOnlyList<CronHealthJobRow>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new Dictionary<string, CronHealthJobRow>(StringComparer.OrdinalIgnoreCase);

        await ReadDashboardRunsAsync(rows, cancellationToken);
        await ReadJobsAsync(rows, cancellationToken);
        await ReadJobsStateAsync(rows, cancellationToken);
        await ReadLatestRunLogsAsync(rows, cancellationToken);

        return rows.Values
            .OrderByDescending(row => row.LastRun ?? DateTime.MinValue)
            .ThenBy(row => row.Name)
            .ToList();
    }

    private async Task ReadDashboardRunsAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
            var latestRuns = await db.CronRuns
                .AsNoTracking()
                .OrderByDescending(run => run.StartedAt ?? run.EndedAt ?? DateTime.MinValue)
                .Take(RunLimit)
                .ToListAsync(cancellationToken);

            foreach (var run in latestRuns)
            {
                var jobId = string.IsNullOrWhiteSpace(run.CronJobId)
                    ? $"db-run-{run.Id}"
                    : run.CronJobId;

                if (rows.ContainsKey(jobId))
                {
                    continue;
                }

                rows[jobId] = new CronHealthJobRow
                {
                    JobId = jobId,
                    Name = string.IsNullOrWhiteSpace(run.JobName) ? jobId : run.JobName,
                    LastRun = run.StartedAt ?? run.EndedAt,
                    Status = NormalizeStatus(run.Status),
                    DurationMs = run.DurationMs,
                    Error = run.Error,
                    Source = "dashboard.db"
                };
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogDebug(ex, "Cron runs table is not available.");
        }
    }

    private async Task ReadJobsAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        var jobsPath = Path.Combine(openclawPaths.Value.CronPath, "jobs.json");

        if (!File.Exists(jobsPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(jobsPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("jobs", out var jobs) ||
                jobs.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var job in jobs.EnumerateArray())
            {
                var jobId = ReadString(job, "id");

                if (string.IsNullOrWhiteSpace(jobId))
                {
                    continue;
                }

                var row = GetOrCreate(rows, jobId);
                row.Name = ReadString(job, "name") ?? row.Name;
                row.Enabled = ReadBool(job, "enabled");
                row.Schedule = ReadSchedule(job) ?? row.Schedule;
                AppendSource(row, "jobs.json");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read cron jobs.json.");
        }
    }

    private async Task ReadJobsStateAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(openclawPaths.Value.CronPath, "jobs-state.json");

        if (!File.Exists(statePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(statePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("jobs", out var jobs) ||
                jobs.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var job in jobs.EnumerateObject())
            {
                var row = GetOrCreate(rows, job.Name);
                row.Enabled ??= ReadEnabledFromScheduleIdentity(job.Value);

                if (!job.Value.TryGetProperty("state", out var state))
                {
                    continue;
                }

                row.LastRun = ReadUnixMs(state, "lastRunAtMs") ?? row.LastRun;
                row.NextRun = ReadUnixMs(state, "nextRunAtMs") ?? row.NextRun;
                row.DurationMs = ReadLong(state, "lastDurationMs") ?? row.DurationMs;
                row.Status = NormalizeStatus(ReadString(state, "lastRunStatus") ?? ReadString(state, "lastStatus") ?? row.Status);
                AppendSource(row, "jobs-state.json");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read cron jobs-state.json.");
        }
    }

    private async Task ReadLatestRunLogsAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        var runsPath = Path.Combine(openclawPaths.Value.CronPath, "runs");

        if (!Directory.Exists(runsPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(runsPath, "*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var lastLine = await ReadLastNonEmptyLineAsync(filePath, cancellationToken);

                if (string.IsNullOrWhiteSpace(lastLine))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(lastLine);
                var root = document.RootElement;
                var jobId = ReadString(root, "jobId") ?? Path.GetFileNameWithoutExtension(filePath);
                var row = GetOrCreate(rows, jobId);

                row.LastRun = ReadUnixMs(root, "runAtMs") ?? ReadUnixMs(root, "ts") ?? row.LastRun;
                row.NextRun = ReadUnixMs(root, "nextRunAtMs") ?? row.NextRun;
                row.DurationMs = ReadLong(root, "durationMs") ?? row.DurationMs;
                row.Status = NormalizeStatus(ReadString(root, "status") ?? row.Status);
                row.Error = ReadString(root, "error") ?? ReadDiagnosticsSummary(root) ?? row.Error;
                AppendSource(row, "run log");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not read cron run log {FilePath}.", filePath);
            }
        }
    }

    private static async Task<string?> ReadLastNonEmptyLineAsync(string filePath, CancellationToken cancellationToken)
    {
        var lastLine = default(string);

        await foreach (var line in File.ReadLinesAsync(filePath, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lastLine = line;
            }
        }

        return lastLine;
    }

    private static CronHealthJobRow GetOrCreate(IDictionary<string, CronHealthJobRow> rows, string jobId)
    {
        if (rows.TryGetValue(jobId, out var row))
        {
            return row;
        }

        row = new CronHealthJobRow
        {
            JobId = jobId,
            Name = jobId,
            Source = "discovered"
        };
        rows[jobId] = row;

        return row;
    }

    private static string? ReadSchedule(JsonElement job)
    {
        if (!job.TryGetProperty("schedule", out var schedule) ||
            schedule.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var expr = ReadString(schedule, "expr");
        var tz = ReadString(schedule, "tz");

        if (string.IsNullOrWhiteSpace(expr))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(tz) ? expr : $"{expr} ({tz})";
    }

    private static bool? ReadEnabledFromScheduleIdentity(JsonElement jobState)
    {
        var scheduleIdentity = ReadString(jobState, "scheduleIdentity");

        if (string.IsNullOrWhiteSpace(scheduleIdentity))
        {
            return null;
        }

        using var document = JsonDocument.Parse(scheduleIdentity);

        return ReadBool(document.RootElement, "enabled");
    }

    private static string? ReadDiagnosticsSummary(JsonElement root)
    {
        return root.TryGetProperty("diagnostics", out var diagnostics) &&
               diagnostics.ValueKind == JsonValueKind.Object
            ? ReadString(diagnostics, "summary")
            : null;
    }

    private static string? ReadString(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static long? ReadLong(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static DateTime? ReadUnixMs(JsonElement source, string propertyName)
    {
        var value = ReadLong(source, propertyName);

        return value is null
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(value.Value).LocalDateTime;
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "unknown" : status;
    }

    private static void AppendSource(CronHealthJobRow row, string source)
    {
        if (string.IsNullOrWhiteSpace(row.Source) || row.Source == "discovered")
        {
            row.Source = source;
            return;
        }

        if (!row.Source.Contains(source, StringComparison.OrdinalIgnoreCase))
        {
            row.Source = $"{row.Source}, {source}";
        }
    }
}
