using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Portfolio.Entities;

namespace Openclaw.Dashboard.Data.Portfolio;

public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    public DbSet<PaperTrade> PaperTrades => Set<PaperTrade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaperTrade>(entity =>
        {
            entity.ToTable("paper_trades");
            entity.HasKey(trade => trade.Id);

            entity.Property(trade => trade.Id).HasColumnName("id");
            entity.Property(trade => trade.Symbol).HasColumnName("symbol");
            entity.Property(trade => trade.Side).HasColumnName("side");
            entity.Property(trade => trade.EntryType).HasColumnName("entry_type");
            entity.Property(trade => trade.EntryPrice).HasColumnName("entry_price");
            entity.Property(trade => trade.Quantity).HasColumnName("quantity");
            entity.Property(trade => trade.ContractDetails).HasColumnName("contract_details");
            entity.Property(trade => trade.EntryDate).HasColumnName("entry_date");
            entity.Property(trade => trade.Thesis).HasColumnName("thesis");
            entity.Property(trade => trade.TpPrice).HasColumnName("tp_price");
            entity.Property(trade => trade.SlPrice).HasColumnName("sl_price");
            entity.Property(trade => trade.InitialConfidence).HasColumnName("initial_confidence");
            entity.Property(trade => trade.Status).HasColumnName("status");
            entity.Property(trade => trade.CloseDate).HasColumnName("close_date");
            entity.Property(trade => trade.ClosePrice).HasColumnName("close_price");
            entity.Property(trade => trade.CloseReason).HasColumnName("close_reason");
            entity.Property(trade => trade.RealizedPnl).HasColumnName("realized_pnl");
            entity.Property(trade => trade.CreatedAt).HasColumnName("created_at");
            entity.Property(trade => trade.UpdatedAt).HasColumnName("updated_at");
            entity.Property(trade => trade.Portfolio).HasColumnName("portfolio");
            entity.Property(trade => trade.SignalId).HasColumnName("signal_id");
            entity.Property(trade => trade.Tier2Snapshot).HasColumnName("tier2_snapshot");
            entity.Property(trade => trade.EntryMonitorStatus).HasColumnName("entry_monitor_status");
            entity.Property(trade => trade.CancelledReason).HasColumnName("cancelled_reason");
        });
    }
}
