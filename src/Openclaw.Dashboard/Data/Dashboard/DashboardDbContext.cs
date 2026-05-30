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
    }
}
