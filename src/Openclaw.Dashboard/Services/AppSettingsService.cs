using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Dashboard.Entities;

namespace Openclaw.Dashboard.Services;

public sealed class AppSettingsService(IDbContextFactory<DashboardDbContext> dashboardDbFactory)
{
    private static readonly IReadOnlyList<AppSettingDefinition> Definitions =
    [
        new("signal.min_confidence", "Signal thresholds", "Minimum confidence", "Signals below this confidence are held for review.", AppSettingValueType.Decimal, "0.70", true, "Changing the minimum confidence threshold can alter which signals reach review.", 0m, 1m),
        new("signal.min_source_score", "Signal thresholds", "Minimum source score", "Lowest source reliability score accepted for signal promotion.", AppSettingValueType.Decimal, "0.55", true, "Changing source score thresholds can admit weaker sources or hide usable signals.", 0m, 1m),
        new("signal.max_age_minutes", "Signal thresholds", "Maximum signal age", "Oldest signal age accepted for active workflows.", AppSettingValueType.Integer, "90", false, null, 1m, 1440m, "min"),

        new("sources.news.enabled", "Source controls", "News sources", "Allow scheduled jobs to use configured news feeds.", AppSettingValueType.Boolean, "true", true, "Disabling news sources can remove a primary signal input."),
        new("sources.social.enabled", "Source controls", "Social sources", "Allow scheduled jobs to use configured social sentiment sources.", AppSettingValueType.Boolean, "false", true, "Enabling social sources can introduce noisier signals."),
        new("sources.max_per_cycle", "Source controls", "Max source pulls", "Maximum source pulls allowed per collection cycle.", AppSettingValueType.Integer, "25", false, null, 1m, 250m),

        new("paper_trading.enabled", "Paper trading", "Paper trading", "Allow paper trade workflows to open simulated positions.", AppSettingValueType.Boolean, "false", true, "Enabling paper trading allows automated simulated position creation."),
        new("paper_trading.default_notional", "Paper trading", "Default notional", "Default simulated position size for new paper trades.", AppSettingValueType.Decimal, "1000", false, null, 1m, 1000000m, "USD"),
        new("paper_trading.max_open_positions", "Paper trading", "Max open positions", "Maximum simultaneous open paper trades.", AppSettingValueType.Integer, "5", true, "Increasing open positions can materially expand simulated exposure.", 1m, 100m),

        new("risk.max_position_pct", "Risk controls", "Max position size", "Largest position size as a percentage of paper portfolio value.", AppSettingValueType.Decimal, "5", true, "Changing max position size changes risk limits.", 0.1m, 100m, "%"),
        new("risk.daily_loss_limit_pct", "Risk controls", "Daily loss limit", "Daily paper portfolio loss limit.", AppSettingValueType.Decimal, "2", true, "Changing the daily loss limit changes the emergency stop boundary.", 0.1m, 100m, "%"),
        new("risk.default_stop_loss_pct", "Risk controls", "Default stop loss", "Default stop loss used when a signal does not provide one.", AppSettingValueType.Decimal, "8", true, "Changing the default stop loss affects downside protection.", 0.1m, 100m, "%"),

        new("model.profile", "Model settings", "Model profile", "Model behavior profile used by local signal analysis jobs.", AppSettingValueType.Select, "balanced", false, null, Options: ["fast", "balanced", "strict"]),
        new("model.temperature", "Model settings", "Temperature", "Sampling temperature for model-assisted analysis.", AppSettingValueType.Decimal, "0.20", false, null, 0m, 2m),
        new("model.lookback_days", "Model settings", "Lookback window", "Historical days available to model-assisted analysis.", AppSettingValueType.Integer, "30", false, null, 1m, 365m, "days")
    ];

    private static readonly IReadOnlyDictionary<string, string> CategoryIcons = new Dictionary<string, string>
    {
        ["Signal thresholds"] = Icons.Material.Filled.Tune,
        ["Source controls"] = Icons.Material.Filled.TravelExplore,
        ["Paper trading"] = Icons.Material.Filled.AssignmentTurnedIn,
        ["Risk controls"] = Icons.Material.Filled.Policy,
        ["Model settings"] = Icons.Material.Filled.Psychology
    };

    public async Task<IReadOnlyList<AppSettingCategory>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSettingsSchemaAsync(db, cancellationToken);
        await SeedDefaultsAsync(db, cancellationToken);

        var settingsByKey = await db.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return Definitions
            .GroupBy(definition => definition.Category)
            .Select(group => new AppSettingCategory(
                group.Key,
                CategoryIcons[group.Key],
                group.Select(definition =>
                {
                    var setting = settingsByKey[definition.Key];
                    return new AppSettingValue(definition, setting.Value, setting.UpdatedAt);
                }).ToList()))
            .ToList();
    }

    public async Task SaveSettingAsync(string key, string value, string actor = "dashboard", CancellationToken cancellationToken = default)
    {
        var definition = Definitions.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown setting '{key}'.");

        var normalizedValue = NormalizeAndValidate(definition, value);

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSettingsSchemaAsync(db, cancellationToken);
        await SeedDefaultsAsync(db, cancellationToken);

        var setting = await db.AppSettings
            .SingleAsync(item => item.Key == definition.Key, cancellationToken);

        if (setting.Value == normalizedValue)
        {
            return;
        }

        var now = DateTime.Now;
        var oldValue = setting.Value;
        setting.Value = normalizedValue;
        setting.UpdatedAt = now;

        db.SettingsAudits.Add(new SettingsAudit
        {
            SettingKey = definition.Key,
            OldValue = oldValue,
            NewValue = normalizedValue,
            Actor = actor,
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureSettingsSchemaAsync(DashboardDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS app_settings (
                Id INTEGER NOT NULL CONSTRAINT PK_app_settings PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Value TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_app_settings_Key ON app_settings (Key);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS settings_audit (
                Id INTEGER NOT NULL CONSTRAINT PK_settings_audit PRIMARY KEY AUTOINCREMENT,
                SettingKey TEXT NOT NULL,
                OldValue TEXT NULL,
                NewValue TEXT NULL,
                Actor TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS signal_reviews (
                Id INTEGER NOT NULL CONSTRAINT PK_signal_reviews PRIMARY KEY AUTOINCREMENT,
                SignalId INTEGER NOT NULL,
                Status TEXT NOT NULL,
                Rating INTEGER NULL,
                Note TEXT NULL,
                Reviewer TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_signal_reviews_SignalId ON signal_reviews (SignalId);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS cron_runs (
                Id INTEGER NOT NULL CONSTRAINT PK_cron_runs PRIMARY KEY AUTOINCREMENT,
                CronJobId TEXT NOT NULL,
                JobName TEXT NOT NULL,
                StartedAt TEXT NULL,
                EndedAt TEXT NULL,
                Status TEXT NOT NULL,
                DurationMs INTEGER NULL,
                SourceRunFile TEXT NULL,
                Summary TEXT NULL,
                Error TEXT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_cron_runs_CronJobId ON cron_runs (CronJobId);",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS dashboard_summary (
                Id INTEGER NOT NULL CONSTRAINT PK_dashboard_summary PRIMARY KEY AUTOINCREMENT,
                Kind TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                SnapshotAt TEXT NOT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_dashboard_summary_SnapshotAt ON dashboard_summary (SnapshotAt);",
            cancellationToken);
    }

    private static async Task SeedDefaultsAsync(DashboardDbContext db, CancellationToken cancellationToken)
    {
        var existingKeys = await db.AppSettings
            .Select(setting => setting.Key)
            .ToListAsync(cancellationToken);
        var existingKeySet = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;

        foreach (var definition in Definitions.Where(definition => !existingKeySet.Contains(definition.Key)))
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = definition.Key,
                Value = definition.DefaultValue,
                CreatedAt = now,
                UpdatedAt = now
            });

            db.SettingsAudits.Add(new SettingsAudit
            {
                SettingKey = definition.Key,
                OldValue = null,
                NewValue = definition.DefaultValue,
                Actor = "seed",
                CreatedAt = now
            });
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string NormalizeAndValidate(AppSettingDefinition definition, string value)
    {
        return definition.ValueType switch
        {
            AppSettingValueType.Boolean => NormalizeBoolean(definition, value),
            AppSettingValueType.Decimal => NormalizeDecimal(definition, value),
            AppSettingValueType.Integer => NormalizeInteger(definition, value),
            AppSettingValueType.Select => NormalizeSelect(definition, value),
            AppSettingValueType.Text => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{definition.Label} is required.") : value.Trim(),
            _ => throw new InvalidOperationException($"Unsupported setting type for {definition.Key}.")
        };
    }

    private static string NormalizeBoolean(AppSettingDefinition definition, string value)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"{definition.Label} must be true or false.");
        }

        return parsed ? "true" : "false";
    }

    private static string NormalizeDecimal(AppSettingDefinition definition, string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"{definition.Label} must be a number.");
        }

        ValidateRange(definition, parsed);
        return parsed.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string NormalizeInteger(AppSettingDefinition definition, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"{definition.Label} must be a whole number.");
        }

        ValidateRange(definition, parsed);
        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeSelect(AppSettingDefinition definition, string value)
    {
        var normalized = value.Trim();
        if (definition.Options?.Contains(normalized, StringComparer.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException($"{definition.Label} must be one of the configured options.");
        }

        return definition.Options.First(option => option.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateRange(AppSettingDefinition definition, decimal value)
    {
        if (definition.Min is not null && value < definition.Min.Value)
        {
            throw new InvalidOperationException($"{definition.Label} must be at least {definition.Min.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (definition.Max is not null && value > definition.Max.Value)
        {
            throw new InvalidOperationException($"{definition.Label} must be no more than {definition.Max.Value.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
}
