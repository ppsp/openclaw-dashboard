using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
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
        await ReadYouTubePipelineHealthAsync(rows, cancellationToken);

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

    private async Task ReadYouTubePipelineHealthAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        await ReadYouTubeQueueHealthAsync(rows, cancellationToken);
        await ReadYouTubeFreshnessHealthAsync(rows, cancellationToken);
        await ReadYouTubeTranscriberHealthAsync(rows, cancellationToken);
    }

    private async Task ReadYouTubeQueueHealthAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        var queuePath = Path.Combine(openclawPaths.Value.WorkspacePath, "data", "classifier_queue.json");
        var row = GetOrCreate(rows, "youtube-classifier-queue-backlog");
        row.Name = "YouTube Classifier Queue Backlog";
        row.Source = "classifier_queue.json";

        if (!File.Exists(queuePath))
        {
            row.Status = "unknown";
            row.Error = "Queue file was not found.";
            return;
        }

        try
        {
            await using var stream = File.OpenRead(queuePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var items = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : [];
            var oldest = items
                .Select(item => ReadDateTime(item, "queued_at"))
                .Where(value => value is not null)
                .Min();
            var byChannel = items
                .Select(item => ReadString(item, "channel") ?? "unknown")
                .GroupBy(channel => channel)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Take(5)
                .Select(group => $"{group.Key}: {group.Count()}");

            row.LastRun = oldest;
            row.Status = items.Count == 0
                ? "ok"
                : oldest is not null && oldest.Value < DateTime.Now.AddHours(-24)
                    ? "warning"
                    : "pending";
            row.Error = items.Count == 0
                ? "Queue is empty."
                : $"Queued={items.Count}; oldest={FormatHealthDate(oldest)}; {string.Join(", ", byChannel)}";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            row.Status = "error";
            row.Error = ex.Message;
        }
    }

    private async Task ReadYouTubeFreshnessHealthAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        var row = GetOrCreate(rows, "youtube-channel-freshness");
        row.Name = "YouTube Channel Freshness";
        row.Source = "youtube_channels.json, signals.db, RSS";

        try
        {
            var channels = await ReadYoutubeChannelsAsync(cancellationToken);
            var latestBySource = await ReadLatestYoutubeSignalsAsync(cancellationToken);
            var stale = new List<string>();
            var summary = new List<string>();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            foreach (var channel in channels.Take(8))
            {
                var dbLatest = latestBySource.TryGetValue($"YouTube/{channel.Name}", out var latest)
                    ? latest
                    : (DateTime?)null;
                var rssLatest = await ReadLatestYoutubeRssPublishedAsync(httpClient, channel.Id, cancellationToken);

                if (rssLatest is not null &&
                    (dbLatest is null || rssLatest.Value.LocalDateTime > dbLatest.Value.AddHours(1)))
                {
                    stale.Add($"{channel.Name}: RSS {FormatHealthDate(rssLatest.Value.LocalDateTime)}, DB {FormatHealthDate(dbLatest)}");
                }

                summary.Add($"{channel.Name}: {FormatHealthDate(dbLatest)}");
            }

            row.Status = stale.Count == 0 ? "ok" : "warning";
            row.Error = stale.Count == 0
                ? $"Latest DB signals: {string.Join("; ", summary.Take(4))}"
                : $"RSS newer than DB: {string.Join("; ", stale.Take(4))}";
            row.LastRun = latestBySource.Count == 0 ? null : latestBySource.Values.Max();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or SqliteException or HttpRequestException or TaskCanceledException)
        {
            row.Status = "error";
            row.Error = ex.Message;
        }
    }

    private async Task ReadYouTubeTranscriberHealthAsync(
        IDictionary<string, CronHealthJobRow> rows,
        CancellationToken cancellationToken)
    {
        var row = GetOrCreate(rows, "youtube-transcriber-health");
        row.Name = "YouTube Transcriber Health";
        row.Source = "youtube_config.json";
        row.LastRun = DateTime.Now;

        var remoteUrl = await ReadYouTubeRemoteUrlAsync(cancellationToken);
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await httpClient.GetAsync($"{remoteUrl.TrimEnd('/')}/health", cancellationToken);
            row.Status = response.IsSuccessStatusCode ? "ok" : "error";
            row.Error = $"{remoteUrl}/health returned {(int)response.StatusCode}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            row.Status = "error";
            row.Error = $"{remoteUrl}/health failed: {ex.Message}";
        }
    }

    private async Task<IReadOnlyList<YoutubeChannelHealth>> ReadYoutubeChannelsAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(openclawPaths.Value.WorkspacePath, "data", "youtube_channels.json");
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
                .EnumerateArray()
                .Select(item => new YoutubeChannelHealth(
                    ReadString(item, "id") ?? string.Empty,
                    ReadString(item, "name") ?? string.Empty))
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Id) && !string.IsNullOrWhiteSpace(channel.Name))
                .ToList()
            : [];
    }

    private async Task<Dictionary<string, DateTime>> ReadLatestYoutubeSignalsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var dbPath = Path.Combine(openclawPaths.Value.SqlitePath, "signals.db");
        if (!File.Exists(dbPath))
        {
            return result;
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source, MAX(discovered_at)
            FROM signals
            WHERE source LIKE 'YouTube/%'
            GROUP BY source;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var source = reader.GetString(0);
            var rawDate = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (TryParseDateTime(rawDate, out var latest))
            {
                result[source] = latest;
            }
        }

        return result;
    }

    private static async Task<DateTimeOffset?> ReadLatestYoutubeRssPublishedAsync(
        HttpClient httpClient,
        string channelId,
        CancellationToken cancellationToken)
    {
        var xml = await httpClient.GetStringAsync(
            $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(channelId)}",
            cancellationToken);
        var document = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var raw = document.Root?
            .Elements(atom + "entry")
            .Select(entry => entry.Element(atom + "published")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return DateTimeOffset.TryParse(raw, out var published) ? published : null;
    }

    private async Task<string> ReadYouTubeRemoteUrlAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(openclawPaths.Value.WorkspacePath, "data", "youtube_config.json");
        if (!File.Exists(path))
        {
            return "http://192.168.1.14:8001";
        }

        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadString(document.RootElement, "remote_url") ?? "http://192.168.1.14:8001";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read YouTube config.");
            return "http://192.168.1.14:8001";
        }
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

    private static DateTime? ReadDateTime(JsonElement source, string propertyName)
    {
        return ReadString(source, propertyName) is { } raw && TryParseDateTime(raw, out var result)
            ? result
            : null;
    }

    private static bool TryParseDateTime(string? value, out DateTime result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTime.TryParse(value, out result);
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

    private static string FormatHealthDate(DateTime? value)
    {
        return value is null ? "none" : value.Value.ToString("g");
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

    private sealed record YoutubeChannelHealth(string Id, string Name);
}
