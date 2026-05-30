using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Portfolio;
using Openclaw.Dashboard.Data.Signals;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class DashboardSummaryService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    IDbContextFactory<SignalsDbContext> signalsDbFactory,
    IDbContextFactory<PortfolioDbContext> portfolioDbFactory,
    IOptions<OpenclawPathsOptions> openclawPaths,
    ILogger<DashboardSummaryService> logger)
{
    private const int QueryLimit = 10_000;
    private const string SummaryKind = "command-center";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CommandCenterSummary> GetCommandCenterSummaryAsync(CancellationToken cancellationToken = default)
    {
        var storedSummary = await TryReadStoredSummaryAsync(cancellationToken);

        if (storedSummary is not null)
        {
            return storedSummary;
        }

        return await ComputeFallbackSummaryAsync(cancellationToken);
    }

    private async Task<CommandCenterSummary?> TryReadStoredSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
            var summary = await db.DashboardSummaries
                .AsNoTracking()
                .Where(item => item.Kind == SummaryKind)
                .OrderByDescending(item => item.SnapshotAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (summary is null)
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<CommandCenterSummary>(summary.PayloadJson, JsonOptions);

            return stored;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogDebug(ex, "Dashboard summary table is not ready; computing fallback summary.");
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Dashboard summary payload could not be parsed; computing fallback summary.");
            return null;
        }
    }

    private async Task<CommandCenterSummary> ComputeFallbackSummaryAsync(CancellationToken cancellationToken)
    {
        var signalMetrics = await ReadSignalMetricsAsync(cancellationToken);
        var openPaperTrades = await ReadOpenPaperTradesAsync(cancellationToken);
        var brokenCrons = await ReadBrokenCronCountAsync(cancellationToken);

        return new CommandCenterSummary
        {
            ActiveSignals = signalMetrics.ActiveSignals,
            Tier1Pass = signalMetrics.Tier1Pass,
            Tier2Complete = signalMetrics.Tier2Complete,
            OpenPaperTrades = openPaperTrades,
            BrokenCrons = brokenCrons,
            TodaysSignals = signalMetrics.TodaysSignals,
            AsOf = DateTime.Now,
            Source = "computed fallback"
        };
    }

    private async Task<SignalMetrics> ReadSignalMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await signalsDbFactory.CreateDbContextAsync(cancellationToken);
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            var recentSignals = db.Signals
                .AsNoTracking()
                .OrderByDescending(signal => signal.Id)
                .Take(QueryLimit);

            var activeSignals = await recentSignals
                .CountAsync(signal =>
                    signal.Status == null ||
                    (signal.Status != "delivered" &&
                     signal.Status != "tier1_reject" &&
                     signal.Status != "rejected" &&
                     signal.OutcomeStatus != "resolved"),
                    cancellationToken);

            var tier1Pass = await recentSignals
                .CountAsync(signal => signal.Tier1Pass == 1 || signal.Status == "tier1_pass", cancellationToken);

            var tier2Complete = await recentSignals
                .CountAsync(signal =>
                    signal.Status == "tier2_complete" ||
                    (signal.Tier2Result != null && signal.Tier2Result != ""),
                    cancellationToken);

            var todaysSignals = await recentSignals
                .CountAsync(signal => signal.DiscoveredAt >= today && signal.DiscoveredAt < tomorrow, cancellationToken);

            return new SignalMetrics(activeSignals, tier1Pass, tier2Complete, todaysSignals);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Could not read signal metrics.");
            return SignalMetrics.Empty;
        }
    }

    private async Task<int> ReadOpenPaperTradesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await portfolioDbFactory.CreateDbContextAsync(cancellationToken);

            return await db.PaperTrades
                .AsNoTracking()
                .OrderByDescending(trade => trade.Id)
                .Take(QueryLimit)
                .CountAsync(trade => trade.Status == "open", cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Could not read paper trade metrics.");
            return 0;
        }
    }

    private async Task<int> ReadBrokenCronCountAsync(CancellationToken cancellationToken)
    {
        var jobsStatePath = Path.Combine(openclawPaths.Value.CronPath, "jobs-state.json");

        if (!File.Exists(jobsStatePath))
        {
            return 0;
        }

        try
        {
            await using var stream = File.OpenRead(jobsStatePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("jobs", out var jobs) ||
                jobs.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            var brokenCount = 0;

            foreach (var job in jobs.EnumerateObject())
            {
                if (!job.Value.TryGetProperty("state", out var state))
                {
                    continue;
                }

                var lastRunStatus = ReadString(state, "lastRunStatus");
                var lastStatus = ReadString(state, "lastStatus");
                var consecutiveErrors = ReadInt(state, "consecutiveErrors");

                if (consecutiveErrors > 0 ||
                    IsBadStatus(lastRunStatus) ||
                    IsBadStatus(lastStatus))
                {
                    brokenCount++;
                }
            }

            return brokenCount;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read cron health metrics.");
            return 0;
        }
    }

    private static string? ReadString(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int ReadInt(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static bool IsBadStatus(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               !status.Equals("ok", StringComparison.OrdinalIgnoreCase) &&
               !status.Equals("delivered", StringComparison.OrdinalIgnoreCase) &&
               !status.Equals("not-requested", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SignalMetrics(int ActiveSignals, int Tier1Pass, int Tier2Complete, int TodaysSignals)
    {
        public static SignalMetrics Empty { get; } = new(0, 0, 0, 0);
    }
}
