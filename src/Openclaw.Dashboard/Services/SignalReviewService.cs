using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Dashboard.Entities;

namespace Openclaw.Dashboard.Services;

public sealed class SignalReviewService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    AdminWriteGuard adminWriteGuard,
    IConfiguration configuration,
    ILogger<SignalReviewService> logger)
{
    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "good",
        "medium",
        "bad"
    };
    private static readonly HashSet<string> AllowedStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "tier0",
        "tier1",
        "tier1_parse",
        "tier1_alpha",
        "tier2"
    };

    public async Task<SignalReviewDto?> GetLatestReviewAsync(
        int signalId,
        string stage = "tier0",
        CancellationToken cancellationToken = default)
    {
        stage = NormalizeStage(stage);
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var reviews = await db.SignalReviews
            .AsNoTracking()
            .Where(item => item.SignalId == signalId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
        var review = reviews.FirstOrDefault(item => ReadStage(item.Status) == stage);

        return review is null
            ? null
            : new SignalReviewDto(
                review.SignalId,
                ReadStage(review.Status),
                ReadDecision(review.Status),
                review.Note,
                review.Reviewer,
                review.UpdatedAt);
    }

    public async Task SaveReviewAsync(SignalReviewRequest request, CancellationToken cancellationToken = default)
    {
        adminWriteGuard.RequireToken(request.AdminToken);

        if (!AllowedDecisions.Contains(request.Decision))
        {
            throw new InvalidOperationException("Decision must be good, medium, or bad.");
        }

        var stage = NormalizeStage(request.Stage);
        var decision = request.Decision.ToLowerInvariant();

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var now = DateTime.Now;
        var reviews = await db.SignalReviews
            .Where(item => item.SignalId == request.SignalId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
        var existing = reviews.FirstOrDefault(item => ReadStage(item.Status) == stage);
        var oldValue = existing is null ? null : SerializeReview(existing);

        if (existing is null)
        {
            existing = new SignalReview
            {
                SignalId = request.SignalId,
                CreatedAt = now
            };
            db.SignalReviews.Add(existing);
        }

        existing.Status = BuildStoredStatus(stage, decision);
        existing.Rating = null;
        existing.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        existing.Reviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? "admin" : request.Reviewer.Trim();
        existing.UpdatedAt = now;

        db.SettingsAudits.Add(new SettingsAudit
        {
            SettingKey = $"signal:{request.SignalId}:{stage}:quality",
            OldValue = oldValue,
            NewValue = SerializeReview(existing),
            Actor = existing.Reviewer ?? "admin",
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await SaveSignalQualityAsync(request.SignalId, stage, decision, cancellationToken);
    }

    public async Task EnsureSignalsQualitySchemaAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("SignalsDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new SqliteConnection(BuildReadWriteConnectionString(connectionString));
        await connection.OpenAsync(cancellationToken);
        await EnsureQualityColumnsAsync(connection, cancellationToken);
    }

    private async Task SaveSignalQualityAsync(int signalId, string stage, string quality, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("SignalsDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            await using var connection = new SqliteConnection(BuildReadWriteConnectionString(connectionString));
            await connection.OpenAsync(cancellationToken);
            await EnsureQualityColumnsAsync(connection, cancellationToken);

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = $"UPDATE signals SET {QualityColumnForStage(stage)} = $quality WHERE id = $id;";
            updateCommand.Parameters.AddWithValue("$quality", quality);
            updateCommand.Parameters.AddWithValue("$id", signalId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Signal quality save failed for signal {SignalId}.", signalId);
        }
    }

    private static string BuildReadWriteConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadWrite
        };

        return builder.ConnectionString;
    }

    private static async Task EnsureQualityColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasSignalQuality = await HasColumnAsync(connection, "signals", "signal_quality", cancellationToken);
        var hasTier0Quality = await HasColumnAsync(connection, "signals", "tier0_quality", cancellationToken);

        if (!hasTier0Quality)
        {
            await AddColumnAsync(connection, "tier0_quality", cancellationToken);
        }

        if (hasSignalQuality)
        {
            await using var migrateCommand = connection.CreateCommand();
            migrateCommand.CommandText = """
                UPDATE signals
                SET tier0_quality = signal_quality
                WHERE tier0_quality IS NULL
                  AND signal_quality IN ('good', 'medium', 'bad');
                """;
            await migrateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "signals", "tier1_quality", cancellationToken))
        {
            await AddColumnAsync(connection, "tier1_quality", cancellationToken);
        }

        if (!await HasColumnAsync(connection, "signals", "tier1_parse_quality", cancellationToken))
        {
            await AddColumnAsync(connection, "tier1_parse_quality", cancellationToken);
        }

        if (!await HasColumnAsync(connection, "signals", "tier1_alpha_quality", cancellationToken))
        {
            await AddColumnAsync(connection, "tier1_alpha_quality", cancellationToken);
        }

        if (!await HasColumnAsync(connection, "signals", "tier2_quality", cancellationToken))
        {
            await AddColumnAsync(connection, "tier2_quality", cancellationToken);
        }
    }

    private static async Task AddColumnAsync(
        SqliteConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE signals ADD COLUMN {columnName} TEXT;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeStage(string? stage)
    {
        if (!AllowedStages.Contains(stage ?? string.Empty))
        {
            throw new InvalidOperationException("Stage must be tier0, tier1_parse, tier1_alpha, tier1, or tier2.");
        }

        return stage!.ToLowerInvariant();
    }

    private static string BuildStoredStatus(string stage, string decision)
    {
        return $"{stage}:{decision}";
    }

    private static string ReadStage(string? storedStatus)
    {
        if (string.IsNullOrWhiteSpace(storedStatus) || !storedStatus.Contains(':'))
        {
            return "tier0";
        }

        var stage = storedStatus.Split(':', 2)[0];
        return AllowedStages.Contains(stage) ? stage.ToLowerInvariant() : "tier0";
    }

    private static string ReadDecision(string? storedStatus)
    {
        if (string.IsNullOrWhiteSpace(storedStatus))
        {
            return "medium";
        }

        var decision = storedStatus.Contains(':')
            ? storedStatus.Split(':', 2)[1]
            : storedStatus;

        return AllowedDecisions.Contains(decision) ? decision.ToLowerInvariant() : "medium";
    }

    private static string QualityColumnForStage(string stage)
    {
        return stage switch
        {
            "tier0" => "tier0_quality",
            "tier1" => "tier1_quality",
            "tier1_parse" => "tier1_parse_quality",
            "tier1_alpha" => "tier1_alpha_quality",
            "tier2" => "tier2_quality",
            _ => throw new InvalidOperationException("Stage must be tier0, tier1_parse, tier1_alpha, tier1, or tier2.")
        };
    }

    private static string SerializeReview(SignalReview review)
    {
        return JsonSerializer.Serialize(new
        {
            review.SignalId,
            Stage = ReadStage(review.Status),
            Quality = ReadDecision(review.Status),
            review.Note,
            review.Reviewer,
            review.UpdatedAt
        });
    }
}
