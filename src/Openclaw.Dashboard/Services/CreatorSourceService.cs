using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Dashboard.Entities;
using Openclaw.Dashboard.Data.Signals;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class CreatorSourceService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    IDbContextFactory<SignalsDbContext> signalsDbFactory,
    IOptions<OpenclawPathsOptions> pathsOptions,
    ILogger<CreatorSourceService> logger)
{
    private static readonly Regex XCreatorRegex = new(
        @"^\s*\[X@(?<handle>[A-Za-z0-9_]{1,30})\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly OpenclawPathsOptions _paths = pathsOptions.Value;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS creator_sources (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Platform TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Handle TEXT NULL,
                ExternalId TEXT NULL,
                Url TEXT NULL,
                Status TEXT NOT NULL DEFAULT 'active',
                TrustLevel TEXT NOT NULL DEFAULT 'normal',
                ScoutEnabled INTEGER NOT NULL DEFAULT 1,
                Notes TEXT NULL,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_creator_sources_Platform_Handle
            ON creator_sources (Platform, Handle);
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS creator_evaluations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatorSourceId INTEGER NOT NULL,
                EvaluatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PeriodStart TEXT NULL,
                PeriodEnd TEXT NULL,
                SignalsCount INTEGER NOT NULL DEFAULT 0,
                GoodCount INTEGER NOT NULL DEFAULT 0,
                MediumCount INTEGER NOT NULL DEFAULT 0,
                BadCount INTEGER NOT NULL DEFAULT 0,
                PassCount INTEGER NOT NULL DEFAULT 0,
                WatchCount INTEGER NOT NULL DEFAULT 0,
                RejectCount INTEGER NOT NULL DEFAULT 0,
                AverageAlphaScore REAL NULL,
                AverageReadinessScore REAL NULL,
                Score REAL NOT NULL DEFAULT 0,
                Summary TEXT NULL,
                FOREIGN KEY (CreatorSourceId) REFERENCES creator_sources(Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_creator_evaluations_CreatorSourceId
            ON creator_evaluations (CreatorSourceId);
            """,
            cancellationToken);
    }

    public async Task<int> SyncKnownCreatorsAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<CreatorSourceEditModel>();
        candidates.AddRange(await ReadXWatchlistAsync(cancellationToken));
        candidates.AddRange(await ReadYoutubeChannelsAsync(cancellationToken));
        candidates.AddRange(await ReadSignalCreatorsAsync(cancellationToken));

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.CreatorSources.ToListAsync(cancellationToken);
        var inserted = 0;

        foreach (var candidate in candidates)
        {
            Normalize(candidate);
            if (string.IsNullOrWhiteSpace(candidate.Handle) || string.IsNullOrWhiteSpace(candidate.DisplayName))
            {
                continue;
            }

            var match = existing.FirstOrDefault(source =>
                source.Platform == candidate.Platform &&
                string.Equals(source.Handle, candidate.Handle, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                if (string.IsNullOrWhiteSpace(match.Url) && !string.IsNullOrWhiteSpace(candidate.Url))
                {
                    match.Url = candidate.Url;
                }

                if (string.IsNullOrWhiteSpace(match.ExternalId) && !string.IsNullOrWhiteSpace(candidate.ExternalId))
                {
                    match.ExternalId = candidate.ExternalId;
                }

                continue;
            }

            var entity = new CreatorSource
            {
                Platform = candidate.Platform,
                DisplayName = candidate.DisplayName,
                Handle = candidate.Handle,
                ExternalId = candidate.ExternalId,
                Url = candidate.Url,
                Status = candidate.Status,
                TrustLevel = candidate.TrustLevel,
                ScoutEnabled = candidate.ScoutEnabled,
                Notes = candidate.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.CreatorSources.Add(entity);
            existing.Add(entity);
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    public async Task<IReadOnlyList<CreatorSourceRow>> GetRowsAsync(
        string? platform,
        string? status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.CreatorSources.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(source => source.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(source => source.Status == status);
        }

        var sources = await query
            .OrderBy(source => source.Platform)
            .ThenBy(source => source.DisplayName)
            .ToListAsync(cancellationToken);
        var stats = await BuildStatsAsync(cancellationToken);
        var latest = await db.CreatorEvaluations
            .AsNoTracking()
            .GroupBy(evaluation => evaluation.CreatorSourceId)
            .Select(group => group.OrderByDescending(evaluation => evaluation.EvaluatedAt).First())
            .ToListAsync(cancellationToken);

        return sources.Select(source =>
        {
            if (!stats.TryGetValue(BuildKey(source.Platform, source.Handle), out var itemStats))
            {
                itemStats = new CreatorStats();
            }
            var latestEval = latest.FirstOrDefault(evaluation => evaluation.CreatorSourceId == source.Id);

            return new CreatorSourceRow(
                source.Id,
                source.Platform,
                source.DisplayName,
                source.Handle,
                source.ExternalId,
                source.Url,
                source.Status,
                source.TrustLevel,
                source.ScoutEnabled,
                source.Notes,
                itemStats.SignalsCount,
                itemStats.GoodCount,
                itemStats.MediumCount,
                itemStats.BadCount,
                itemStats.PassCount,
                itemStats.WatchCount,
                itemStats.RejectCount,
                itemStats.AverageAlphaScore,
                itemStats.AverageReadinessScore,
                latestEval?.Score,
                latestEval?.EvaluatedAt);
        }).ToList();
    }

    public async Task<CreatorSourceEditModel?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.CreatorSources.FindAsync([id], cancellationToken);
        return source is null ? null : ToEditModel(source);
    }

    public async Task SaveAsync(CreatorSourceEditModel model, CancellationToken cancellationToken = default)
    {
        Normalize(model);
        if (string.IsNullOrWhiteSpace(model.DisplayName))
        {
            throw new InvalidOperationException("Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Handle))
        {
            throw new InvalidOperationException("Handle or channel key is required.");
        }

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        CreatorSource entity;
        if (model.Id == 0)
        {
            entity = new CreatorSource { CreatedAt = DateTime.UtcNow };
            db.CreatorSources.Add(entity);
        }
        else
        {
            entity = await db.CreatorSources.FindAsync([model.Id], cancellationToken)
                ?? throw new InvalidOperationException("Creator source not found.");
        }

        entity.Platform = model.Platform;
        entity.DisplayName = model.DisplayName;
        entity.Handle = model.Handle;
        entity.ExternalId = model.ExternalId;
        entity.Url = model.Url;
        entity.Status = model.Status;
        entity.TrustLevel = model.TrustLevel;
        entity.ScoutEnabled = model.ScoutEnabled;
        entity.Notes = model.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.CreatorSources.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return;
        }

        db.CreatorSources.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CreatorEvaluationRow> EvaluateAsync(int creatorSourceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.CreatorSources.FindAsync([creatorSourceId], cancellationToken)
            ?? throw new InvalidOperationException("Creator source not found.");
        var stats = await BuildStatsAsync(cancellationToken);
        if (!stats.TryGetValue(BuildKey(source.Platform, source.Handle), out var itemStats))
        {
            itemStats = new CreatorStats();
        }
        var score = CalculateScore(itemStats);
        var evaluation = new CreatorEvaluation
        {
            CreatorSourceId = source.Id,
            EvaluatedAt = DateTime.UtcNow,
            PeriodStart = itemStats.FirstSignalAt,
            PeriodEnd = itemStats.LastSignalAt,
            SignalsCount = itemStats.SignalsCount,
            GoodCount = itemStats.GoodCount,
            MediumCount = itemStats.MediumCount,
            BadCount = itemStats.BadCount,
            PassCount = itemStats.PassCount,
            WatchCount = itemStats.WatchCount,
            RejectCount = itemStats.RejectCount,
            AverageAlphaScore = itemStats.AverageAlphaScore,
            AverageReadinessScore = itemStats.AverageReadinessScore,
            Score = score,
            Summary = BuildSummary(itemStats, score)
        };

        db.CreatorEvaluations.Add(evaluation);
        await db.SaveChangesAsync(cancellationToken);
        return ToEvaluationRow(evaluation);
    }

    public async Task<int> EvaluateAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var sources = await db.CreatorSources
            .Where(source => source.Status != "archived")
            .ToListAsync(cancellationToken);
        var stats = await BuildStatsAsync(cancellationToken);

        foreach (var source in sources)
        {
            if (!stats.TryGetValue(BuildKey(source.Platform, source.Handle), out var itemStats))
            {
                itemStats = new CreatorStats();
            }
            var score = CalculateScore(itemStats);
            db.CreatorEvaluations.Add(new CreatorEvaluation
            {
                CreatorSourceId = source.Id,
                EvaluatedAt = DateTime.UtcNow,
                PeriodStart = itemStats.FirstSignalAt,
                PeriodEnd = itemStats.LastSignalAt,
                SignalsCount = itemStats.SignalsCount,
                GoodCount = itemStats.GoodCount,
                MediumCount = itemStats.MediumCount,
                BadCount = itemStats.BadCount,
                PassCount = itemStats.PassCount,
                WatchCount = itemStats.WatchCount,
                RejectCount = itemStats.RejectCount,
                AverageAlphaScore = itemStats.AverageAlphaScore,
                AverageReadinessScore = itemStats.AverageReadinessScore,
                Score = score,
                Summary = BuildSummary(itemStats, score)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return sources.Count;
    }

    public async Task<IReadOnlyList<CreatorEvaluationRow>> GetEvaluationHistoryAsync(
        int creatorSourceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        var evaluations = await db.CreatorEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.CreatorSourceId == creatorSourceId)
            .OrderByDescending(evaluation => evaluation.EvaluatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return evaluations.Select(ToEvaluationRow).ToList();
    }

    private async Task<IReadOnlyList<CreatorSourceEditModel>> ReadXWatchlistAsync(CancellationToken cancellationToken)
    {
        var dbPath = Path.Combine(_paths.SqlitePath, "x-watchlist.db");
        if (!File.Exists(dbPath))
        {
            return [];
        }

        var results = new List<CreatorSourceEditModel>();
        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT username, url, enabled FROM watchlist ORDER BY username";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var username = reader.GetString(0);
                results.Add(new CreatorSourceEditModel
                {
                    Platform = "x",
                    DisplayName = $"@{username}",
                    Handle = username,
                    Url = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ScoutEnabled = !reader.IsDBNull(2) && reader.GetInt32(2) == 1,
                    Status = "active"
                });
            }
        }
        catch (SqliteException ex)
        {
            logger.LogWarning(ex, "Could not read X watchlist source DB.");
        }

        return results;
    }

    private async Task<IReadOnlyList<CreatorSourceEditModel>> ReadYoutubeChannelsAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.WorkspacePath, "data", "youtube_channels.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var channels = await JsonSerializer.DeserializeAsync<List<YoutubeChannel>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
            return channels?
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Name))
                .Select(channel => new CreatorSourceEditModel
                {
                    Platform = "youtube",
                    DisplayName = channel.Name!,
                    Handle = channel.Name!,
                    ExternalId = channel.Id,
                    Url = string.IsNullOrWhiteSpace(channel.Id) ? null : $"https://www.youtube.com/channel/{channel.Id}",
                    Status = "active",
                    ScoutEnabled = true
                })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Could not read YouTube channel list.");
            return [];
        }
    }

    private async Task<IReadOnlyList<CreatorSourceEditModel>> ReadSignalCreatorsAsync(CancellationToken cancellationToken)
    {
        await using var db = await signalsDbFactory.CreateDbContextAsync(cancellationToken);
        var signals = await db.Signals
            .AsNoTracking()
            .Where(signal => signal.Source == "youtube" || signal.Source == "x-watchlist" || signal.Source == "X-Watchlist")
            .Select(signal => new { signal.Source, signal.RawSignal, signal.Url })
            .Take(5000)
            .ToListAsync(cancellationToken);

        return signals
            .Select(signal => ResolveCreator(signal.Source, signal.RawSignal, signal.Url))
            .Where(model => model is not null)
            .Cast<CreatorSourceEditModel>()
            .ToList();
    }

    private async Task<Dictionary<string, CreatorStats>> BuildStatsAsync(CancellationToken cancellationToken)
    {
        await using var db = await signalsDbFactory.CreateDbContextAsync(cancellationToken);
        var signals = await db.Signals
            .AsNoTracking()
            .Where(signal => signal.Source == "youtube" || signal.Source == "x-watchlist" || signal.Source == "X-Watchlist")
            .Select(signal => new
            {
                signal.Source,
                signal.RawSignal,
                signal.Url,
                signal.DiscoveredAt,
                signal.Tier1Route,
                signal.Status,
                signal.AlphaScore,
                signal.ReadinessScore,
                signal.Tier0Quality
            })
            .Take(10000)
            .ToListAsync(cancellationToken);

        var stats = new Dictionary<string, CreatorStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in signals)
        {
            var creator = ResolveCreator(signal.Source, signal.RawSignal, signal.Url);
            if (creator?.Handle is null)
            {
                continue;
            }

            var key = BuildKey(creator.Platform, creator.Handle);
            if (!stats.TryGetValue(key, out var itemStats))
            {
                itemStats = new CreatorStats();
                stats[key] = itemStats;
            }

            itemStats.Add(signal.DiscoveredAt, ResolveRoute(signal.Tier1Route, signal.Status), signal.AlphaScore, signal.ReadinessScore, signal.Tier0Quality);
        }

        return stats;
    }

    private static CreatorSourceEditModel? ResolveCreator(string? source, string? rawSignal, string? url)
    {
        var youtubeCreator = ReadStringFromJson(rawSignal, "channel");
        if (!string.IsNullOrWhiteSpace(youtubeCreator))
        {
            return new CreatorSourceEditModel
            {
                Platform = "youtube",
                DisplayName = youtubeCreator,
                Handle = youtubeCreator,
                Url = url,
                Status = "active"
            };
        }

        if (!string.IsNullOrWhiteSpace(rawSignal))
        {
            var xMatch = XCreatorRegex.Match(rawSignal);
            if (xMatch.Success)
            {
                var handle = xMatch.Groups["handle"].Value;
                return new CreatorSourceEditModel
                {
                    Platform = "x",
                    DisplayName = $"@{handle}",
                    Handle = handle,
                    Url = url,
                    Status = "active"
                };
            }
        }

        if (source?.Contains("youtube", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new CreatorSourceEditModel
            {
                Platform = "youtube",
                DisplayName = "YouTube unknown",
                Handle = "unknown",
                Url = url,
                Status = "candidate"
            };
        }

        return null;
    }

    private static string? ReadStringFromJson(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveRoute(string? route, string? status)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            return route.Trim().ToLowerInvariant();
        }

        return status switch
        {
            "tier1_watch" => "watch",
            "tier1_pass" => "pass",
            "tier1_reject" => "reject",
            _ => string.Empty
        };
    }

    private static double CalculateScore(CreatorStats stats)
    {
        if (stats.SignalsCount == 0)
        {
            return 0;
        }

        var quality = (stats.GoodCount * 18) + (stats.MediumCount * 7) - (stats.BadCount * 12);
        var routing = (stats.PassCount * 8) + (stats.WatchCount * 4) - (stats.RejectCount * 5);
        var alpha = stats.AverageAlphaScore is null ? 0 : (stats.AverageAlphaScore.Value - 50) * 0.45;
        return Math.Round(Math.Clamp(50 + quality + routing + alpha, 0, 100), 1);
    }

    private static string BuildSummary(CreatorStats stats, double score)
    {
        if (stats.SignalsCount == 0)
        {
            return "No matched signals yet. Keep as candidate until the scout produces data.";
        }

        return $"Score {score:0.0}: {stats.SignalsCount} signals, {stats.PassCount} pass, {stats.WatchCount} watch, {stats.RejectCount} reject, {stats.GoodCount}/{stats.MediumCount}/{stats.BadCount} good/medium/bad labels.";
    }

    private static CreatorSourceEditModel ToEditModel(CreatorSource source)
    {
        return new CreatorSourceEditModel
        {
            Id = source.Id,
            Platform = source.Platform,
            DisplayName = source.DisplayName,
            Handle = source.Handle,
            ExternalId = source.ExternalId,
            Url = source.Url,
            Status = source.Status,
            TrustLevel = source.TrustLevel,
            ScoutEnabled = source.ScoutEnabled,
            Notes = source.Notes
        };
    }

    private static CreatorEvaluationRow ToEvaluationRow(CreatorEvaluation evaluation)
    {
        return new CreatorEvaluationRow(
            evaluation.Id,
            evaluation.CreatorSourceId,
            evaluation.EvaluatedAt,
            evaluation.PeriodStart,
            evaluation.PeriodEnd,
            evaluation.SignalsCount,
            evaluation.GoodCount,
            evaluation.MediumCount,
            evaluation.BadCount,
            evaluation.PassCount,
            evaluation.WatchCount,
            evaluation.RejectCount,
            evaluation.AverageAlphaScore,
            evaluation.AverageReadinessScore,
            evaluation.Score,
            evaluation.Summary);
    }

    private static void Normalize(CreatorSourceEditModel model)
    {
        model.Platform = (model.Platform ?? string.Empty).Trim().ToLowerInvariant();
        model.DisplayName = (model.DisplayName ?? string.Empty).Trim();
        model.Handle = model.Handle?.Trim().TrimStart('@');
        model.Status = string.IsNullOrWhiteSpace(model.Status) ? "active" : model.Status.Trim().ToLowerInvariant();
        model.TrustLevel = string.IsNullOrWhiteSpace(model.TrustLevel) ? "normal" : model.TrustLevel.Trim().ToLowerInvariant();

        if (model.Platform == "youtube" && string.IsNullOrWhiteSpace(model.Handle))
        {
            model.Handle = model.DisplayName;
        }
    }

    private static string BuildKey(string? platform, string? handle)
    {
        return $"{platform?.Trim().ToLowerInvariant()}:{handle?.Trim().TrimStart('@').ToLowerInvariant()}";
    }

    private sealed class YoutubeChannel
    {
        public string? Id { get; set; }

        public string? Name { get; set; }
    }

    private sealed class CreatorStats
    {
        private int _alphaTotal;
        private int _alphaCount;
        private int _readinessTotal;
        private int _readinessCount;

        public int SignalsCount { get; private set; }
        public int GoodCount { get; private set; }
        public int MediumCount { get; private set; }
        public int BadCount { get; private set; }
        public int PassCount { get; private set; }
        public int WatchCount { get; private set; }
        public int RejectCount { get; private set; }
        public DateTime? FirstSignalAt { get; private set; }
        public DateTime? LastSignalAt { get; private set; }
        public double? AverageAlphaScore => _alphaCount == 0 ? null : Math.Round((double)_alphaTotal / _alphaCount, 1);
        public double? AverageReadinessScore => _readinessCount == 0 ? null : Math.Round((double)_readinessTotal / _readinessCount, 1);

        public void Add(DateTime? discoveredAt, string route, int? alphaScore, int? readinessScore, string? quality)
        {
            SignalsCount++;
            if (discoveredAt is not null)
            {
                FirstSignalAt = FirstSignalAt is null || discoveredAt < FirstSignalAt ? discoveredAt : FirstSignalAt;
                LastSignalAt = LastSignalAt is null || discoveredAt > LastSignalAt ? discoveredAt : LastSignalAt;
            }

            switch (route)
            {
                case "pass":
                case "fast_track":
                    PassCount++;
                    break;
                case "watch":
                    WatchCount++;
                    break;
                case "reject":
                    RejectCount++;
                    break;
            }

            switch (quality)
            {
                case "good":
                    GoodCount++;
                    break;
                case "medium":
                    MediumCount++;
                    break;
                case "bad":
                    BadCount++;
                    break;
            }

            if (alphaScore is not null)
            {
                _alphaTotal += alphaScore.Value;
                _alphaCount++;
            }

            if (readinessScore is not null)
            {
                _readinessTotal += readinessScore.Value;
                _readinessCount++;
            }
        }
    }
}
