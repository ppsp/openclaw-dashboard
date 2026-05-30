using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Dashboard.Entities;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class SignalReviewService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    AdminWriteGuard adminWriteGuard,
    IConfiguration configuration,
    IOptions<OpenclawPathsOptions> openclawOptions,
    ILogger<SignalReviewService> logger)
{
    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "approve",
        "reject",
        "watch"
    };

    public async Task<SignalReviewDto?> GetLatestReviewAsync(int signalId, CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var review = await db.SignalReviews
            .AsNoTracking()
            .Where(item => item.SignalId == signalId)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return review is null
            ? null
            : new SignalReviewDto(review.SignalId, review.Status, review.Rating, review.Note, review.Reviewer, review.UpdatedAt);
    }

    public async Task SaveReviewAsync(SignalReviewRequest request, CancellationToken cancellationToken = default)
    {
        adminWriteGuard.RequireToken(request.AdminToken);

        if (!AllowedDecisions.Contains(request.Decision))
        {
            throw new InvalidOperationException("Decision must be approve, reject, or watch.");
        }

        if (request.Rating is < 1 or > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5.");
        }

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var now = DateTime.Now;
        var existing = await db.SignalReviews
            .Where(item => item.SignalId == request.SignalId)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
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

        existing.Status = request.Decision.ToLowerInvariant();
        existing.Rating = request.Rating;
        existing.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        existing.Reviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? "admin" : request.Reviewer.Trim();
        existing.UpdatedAt = now;

        db.SettingsAudits.Add(new SettingsAudit
        {
            SettingKey = $"signal:{request.SignalId}:review",
            OldValue = oldValue,
            NewValue = SerializeReview(existing),
            Actor = existing.Reviewer ?? "admin",
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await TryMirrorRatingAsync(request.SignalId, request.Rating, cancellationToken);
    }

    private async Task TryMirrorRatingAsync(int signalId, int rating, CancellationToken cancellationToken)
    {
        if (!openclawOptions.Value.MirrorSignalRatingToSignalsDb)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("SignalsDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString)
            {
                Mode = SqliteOpenMode.ReadWrite
            };

            await using var connection = new SqliteConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "PRAGMA table_info(signals);";

            var hasRatingColumn = false;
            await using (var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetString(1).Equals("rating", StringComparison.OrdinalIgnoreCase))
                    {
                        hasRatingColumn = true;
                        break;
                    }
                }
            }

            if (!hasRatingColumn)
            {
                return;
            }

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = "UPDATE signals SET rating = $rating WHERE id = $id;";
            updateCommand.Parameters.AddWithValue("$rating", rating);
            updateCommand.Parameters.AddWithValue("$id", signalId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Signal rating mirror failed for signal {SignalId}.", signalId);
        }
    }

    private static string SerializeReview(SignalReview review)
    {
        return JsonSerializer.Serialize(new
        {
            review.SignalId,
            Decision = review.Status,
            review.Rating,
            review.Note,
            review.Reviewer,
            review.UpdatedAt
        });
    }
}
