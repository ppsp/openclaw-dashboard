using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Dashboard.Entities;

namespace Openclaw.Dashboard.Data.Dashboard;

public sealed class DashboardDbContext(DbContextOptions<DashboardDbContext> options) : DbContext(options)
{
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<SettingsAudit> SettingsAudits => Set<SettingsAudit>();

    public DbSet<SignalReview> SignalReviews => Set<SignalReview>();

    public DbSet<CronRun> CronRuns => Set<CronRun>();

    public DbSet<DashboardSummary> DashboardSummaries => Set<DashboardSummary>();

    public DbSet<CreatorSource> CreatorSources => Set<CreatorSource>();

    public DbSet<CreatorEvaluation> CreatorEvaluations => Set<CreatorEvaluation>();

    public DbSet<TickerWatchlistItem> TickerWatchlistItems => Set<TickerWatchlistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("app_settings");
            entity.HasKey(setting => setting.Id);
            entity.HasIndex(setting => setting.Key).IsUnique();
            entity.Property(setting => setting.Key).HasMaxLength(160);
            entity.Property(setting => setting.Value).HasColumnType("TEXT");
        });

        modelBuilder.Entity<SettingsAudit>(entity =>
        {
            entity.ToTable("settings_audit");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.SettingKey).HasMaxLength(160);
        });

        modelBuilder.Entity<SignalReview>(entity =>
        {
            entity.ToTable("signal_reviews");
            entity.HasKey(review => review.Id);
            entity.HasIndex(review => review.SignalId);
            entity.Property(review => review.Status).HasMaxLength(80);
        });

        modelBuilder.Entity<CronRun>(entity =>
        {
            entity.ToTable("cron_runs");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => run.CronJobId);
            entity.Property(run => run.CronJobId).HasMaxLength(120);
            entity.Property(run => run.JobName).HasMaxLength(240);
            entity.Property(run => run.Status).HasMaxLength(80);
        });

        modelBuilder.Entity<DashboardSummary>(entity =>
        {
            entity.ToTable("dashboard_summary");
            entity.HasKey(summary => summary.Id);
            entity.HasIndex(summary => summary.SnapshotAt);
            entity.Property(summary => summary.Kind).HasMaxLength(80);
            entity.Property(summary => summary.PayloadJson).HasColumnType("TEXT");
        });

        modelBuilder.Entity<CreatorSource>(entity =>
        {
            entity.ToTable("creator_sources");
            entity.HasKey(source => source.Id);
            entity.HasIndex(source => new { source.Platform, source.Handle }).IsUnique();
            entity.Property(source => source.Platform).HasMaxLength(24);
            entity.Property(source => source.DisplayName).HasMaxLength(160);
            entity.Property(source => source.Handle).HasMaxLength(160);
            entity.Property(source => source.ExternalId).HasMaxLength(180);
            entity.Property(source => source.Status).HasMaxLength(40);
            entity.Property(source => source.TrustLevel).HasMaxLength(40);
            entity.Property(source => source.Notes).HasColumnType("TEXT");
        });

        modelBuilder.Entity<CreatorEvaluation>(entity =>
        {
            entity.ToTable("creator_evaluations");
            entity.HasKey(evaluation => evaluation.Id);
            entity.HasIndex(evaluation => evaluation.CreatorSourceId);
            entity.HasOne(evaluation => evaluation.CreatorSource)
                .WithMany()
                .HasForeignKey(evaluation => evaluation.CreatorSourceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(evaluation => evaluation.Summary).HasColumnType("TEXT");
        });

        modelBuilder.Entity<TickerWatchlistItem>(entity =>
        {
            entity.ToTable("ticker_watchlist_items");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Symbol).IsUnique();
            entity.HasIndex(item => item.AssetClass);
            entity.HasIndex(item => item.Status);
            entity.Property(item => item.Symbol).HasMaxLength(32);
            entity.Property(item => item.AssetClass).HasMaxLength(24);
            entity.Property(item => item.Sector).HasMaxLength(120);
            entity.Property(item => item.Description).HasColumnType("TEXT");
            entity.Property(item => item.WatchReason).HasColumnType("TEXT");
            entity.Property(item => item.Status).HasMaxLength(40);
            entity.Property(item => item.TimeHorizon).HasMaxLength(120);
            entity.Property(item => item.Notes).HasColumnType("TEXT");
        });
    }
}
