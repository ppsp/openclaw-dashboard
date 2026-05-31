using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Signals.Entities;

namespace Openclaw.Dashboard.Data.Signals;

public sealed class SignalsDbContext(DbContextOptions<SignalsDbContext> options) : DbContext(options)
{
    public DbSet<Signal> Signals => Set<Signal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Signal>(entity =>
        {
            entity.ToTable("signals");
            entity.HasKey(signal => signal.Id);

            entity.Property(signal => signal.Id).HasColumnName("id");
            entity.Property(signal => signal.RawSignal).HasColumnName("raw_signal");
            entity.Property(signal => signal.Source).HasColumnName("source");
            entity.Property(signal => signal.Url).HasColumnName("url");
            entity.Property(signal => signal.DiscoveredAt).HasColumnName("discovered_at");
            entity.Property(signal => signal.Status).HasColumnName("status");
            entity.Property(signal => signal.Fingerprint).HasColumnName("fingerprint");
            entity.Property(signal => signal.Sources).HasColumnName("sources");
            entity.Property(signal => signal.Tier1Score).HasColumnName("tier1_score");
            entity.Property(signal => signal.Tier1Dims).HasColumnName("tier1_dims");
            entity.Property(signal => signal.Tier1Pass).HasColumnName("tier1_pass");
            entity.Property(signal => signal.Tier1Route).HasColumnName("tier1_route");
            entity.Property(signal => signal.AlphaScore).HasColumnName("alpha_score");
            entity.Property(signal => signal.ReadinessScore).HasColumnName("readiness_score");
            entity.Property(signal => signal.ReasonCategory).HasColumnName("reason_category");
            entity.Property(signal => signal.NextAction).HasColumnName("next_action");
            entity.Property(signal => signal.Tier2Result).HasColumnName("tier2_result");
            entity.Property(signal => signal.DeliveredAt).HasColumnName("delivered_at");
            entity.Property(signal => signal.Rating).HasColumnName("rating");
            entity.Property(signal => signal.OutcomeStatus).HasColumnName("outcome_status");
            entity.Property(signal => signal.TriggeredAt).HasColumnName("triggered_at");
            entity.Property(signal => signal.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(signal => signal.OutcomeNote).HasColumnName("outcome_note");
            entity.Property(signal => signal.MonitoringStart).HasColumnName("monitoring_start");
            entity.Property(signal => signal.TtlDays).HasColumnName("ttl_days");
            entity.Property(signal => signal.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
