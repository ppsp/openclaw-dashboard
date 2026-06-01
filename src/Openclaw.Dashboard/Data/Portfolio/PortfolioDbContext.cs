using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Portfolio.Entities;

namespace Openclaw.Dashboard.Data.Portfolio;

public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    public DbSet<LivePortfolioHolding> LivePortfolio => Set<LivePortfolioHolding>();

    public DbSet<PaperTrade> PaperTrades => Set<PaperTrade>();

    public DbSet<PortfolioTransaction> TransactionHistory => Set<PortfolioTransaction>();

    public DbSet<TradeCheckup> TradeCheckups => Set<TradeCheckup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LivePortfolioHolding>(entity =>
        {
            entity.ToTable("live_portfolio");
            entity.HasKey(holding => new { holding.Ticker, holding.AccountId });

            entity.Property(holding => holding.Ticker).HasColumnName("Ticker");
            entity.Property(holding => holding.NetAmount).HasColumnName("netAmount");
            entity.Property(holding => holding.AccountId).HasColumnName("accountId");
            entity.Property(holding => holding.AvgPriceCad).HasColumnName("avgPriceCAD");
            entity.Property(holding => holding.LastUpdate).HasColumnName("lastUpdate");
            entity.Property(holding => holding.AvgPriceUsd).HasColumnName("avgPriceUSD");
        });

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

        modelBuilder.Entity<PortfolioTransaction>(entity =>
        {
            entity.ToTable("transaction_history");
            entity.HasKey(transaction => transaction.Id);

            entity.Property(transaction => transaction.Id).HasColumnName("Id");
            entity.Property(transaction => transaction.InsertDate).HasColumnName("InsertDate");
            entity.Property(transaction => transaction.Ticker).HasColumnName("Ticker");
            entity.Property(transaction => transaction.Amount).HasColumnName("Amount");
            entity.Property(transaction => transaction.PriceCad).HasColumnName("priceCAD");
            entity.Property(transaction => transaction.PriceUsd).HasColumnName("priceUSD");
            entity.Property(transaction => transaction.AccountId).HasColumnName("accountId");
            entity.Property(transaction => transaction.Comment).HasColumnName("comment");
        });

        modelBuilder.Entity<TradeCheckup>(entity =>
        {
            entity.ToTable("trade_checkups");
            entity.HasKey(checkup => checkup.Id);

            entity.Property(checkup => checkup.Id).HasColumnName("id");
            entity.Property(checkup => checkup.TradeId).HasColumnName("trade_id");
            entity.Property(checkup => checkup.CheckupDate).HasColumnName("checkup_date");
            entity.Property(checkup => checkup.CurrentPrice).HasColumnName("current_price");
            entity.Property(checkup => checkup.UnrealizedPnl).HasColumnName("unrealized_pnl");
            entity.Property(checkup => checkup.PnlPct).HasColumnName("pnl_pct");
            entity.Property(checkup => checkup.DaysHeld).HasColumnName("days_held");
            entity.Property(checkup => checkup.ThesisStillValid).HasColumnName("thesis_still_valid");
            entity.Property(checkup => checkup.ConfidenceCurrent).HasColumnName("confidence_current");
            entity.Property(checkup => checkup.Recommendation).HasColumnName("recommendation");
            entity.Property(checkup => checkup.Notes).HasColumnName("notes");
        });
    }
}
