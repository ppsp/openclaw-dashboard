using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Portfolio;
using Openclaw.Dashboard.Data.Portfolio.Entities;

namespace Openclaw.Dashboard.Services;

public sealed class PaperTradeQueryService(
    IDbContextFactory<PortfolioDbContext> portfolioDbFactory,
    ILogger<PaperTradeQueryService> logger)
{
    private const int QueryLimit = 10_000;

    public async Task<IReadOnlyList<PaperTradeSummary>> GetPortfolioSummariesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await portfolioDbFactory.CreateDbContextAsync(cancellationToken);
            var trades = await db.PaperTrades
                .AsNoTracking()
                .OrderByDescending(trade => trade.Id)
                .Take(QueryLimit)
                .ToListAsync(cancellationToken);

            return BuildSummaries(trades);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Paper trade summary lookup failed.");
            return [];
        }
    }

    public async Task<PagedResult<PaperTradeRow>> SearchAsync(
        PaperTradeStatusFilter statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(0, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        try
        {
            await using var db = await portfolioDbFactory.CreateDbContextAsync(cancellationToken);
            var query = ApplyStatusFilter(db.PaperTrades.AsNoTracking(), statusFilter);
            var totalItems = await query.CountAsync(cancellationToken);
            var trades = await query
                .OrderBy(trade => trade.Status == "open" ? 0 : 1)
                .ThenByDescending(trade => trade.Id)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<PaperTradeRow>(trades.Select(ToRow).ToList(), totalItems);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Paper trade search failed.");
            return new PagedResult<PaperTradeRow>([], 0);
        }
    }

    public async Task<PagedResult<PaperTradeHistoryRow>> SearchHistoryAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(0, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        try
        {
            await using var db = await portfolioDbFactory.CreateDbContextAsync(cancellationToken);
            var query =
                from checkup in db.TradeCheckups.AsNoTracking()
                join trade in db.PaperTrades.AsNoTracking() on checkup.TradeId equals trade.Id into tradeGroup
                from trade in tradeGroup.DefaultIfEmpty()
                select new
                {
                    Checkup = checkup,
                    Ticker = trade == null ? string.Empty : trade.Symbol,
                    Portfolio = trade == null ? string.Empty : trade.Portfolio,
                    Status = trade == null ? string.Empty : trade.Status
                };

            var totalItems = await query.CountAsync(cancellationToken);
            var history = await query
                .OrderByDescending(row => row.Checkup.CheckupDate)
                .ThenByDescending(row => row.Checkup.Id)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<PaperTradeHistoryRow>(
                history.Select(row => ToHistoryRow(row.Checkup, row.Ticker, row.Portfolio, row.Status)).ToList(),
                totalItems);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Paper trade history search failed.");
            return new PagedResult<PaperTradeHistoryRow>([], 0);
        }
    }

    private static IReadOnlyList<PaperTradeSummary> BuildSummaries(IReadOnlyList<PaperTrade> trades)
    {
        var knownPortfolios = new[] { "manual", "auto" };
        var discoveredPortfolios = trades
            .Select(NormalizePortfolio)
            .Where(portfolio => !knownPortfolios.Contains(portfolio, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(portfolio => portfolio);

        return knownPortfolios
            .Concat(discoveredPortfolios)
            .Select(portfolio => BuildSummary(portfolio, trades.Where(trade =>
                NormalizePortfolio(trade).Equals(portfolio, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    private static PaperTradeSummary BuildSummary(string portfolio, IEnumerable<PaperTrade> portfolioTrades)
    {
        var trades = portfolioTrades.ToList();
        var realizedPnl = trades.Sum(trade => trade.RealizedPnl ?? 0m);
        var totalCostBasis = trades
            .Where(trade => trade.RealizedPnl is not null)
            .Sum(CostBasis);

        return new PaperTradeSummary(
            FormatPortfolio(portfolio),
            trades.Count(IsOpen),
            trades.Count(trade => !IsOpen(trade)),
            realizedPnl,
            totalCostBasis == 0m ? null : realizedPnl / totalCostBasis * 100m,
            trades.Count(trade => trade.SignalId is not null));
    }

    private static IQueryable<PaperTrade> ApplyStatusFilter(
        IQueryable<PaperTrade> query,
        PaperTradeStatusFilter statusFilter)
    {
        return statusFilter switch
        {
            PaperTradeStatusFilter.Open => query.Where(IsOpenExpression()),
            PaperTradeStatusFilter.Closed => query.Where(trade => trade.Status != "open"),
            _ => query
        };
    }

    private static System.Linq.Expressions.Expression<Func<PaperTrade, bool>> IsOpenExpression()
    {
        return trade => trade.Status == null || trade.Status == "" || trade.Status == "open";
    }

    private static PaperTradeRow ToRow(PaperTrade trade)
    {
        return new PaperTradeRow(
            trade.Id,
            trade.Symbol,
            trade.Side,
            trade.EntryPrice,
            trade.Quantity,
            trade.ClosePrice,
            trade.TpPrice,
            trade.SlPrice,
            string.IsNullOrWhiteSpace(trade.Status) ? "open" : trade.Status,
            trade.RealizedPnl,
            CalculatePnlPct(trade),
            FormatPortfolio(NormalizePortfolio(trade)),
            trade.SignalId,
            trade.EntryType,
            trade.EntryDate);
    }

    private static PaperTradeHistoryRow ToHistoryRow(
        TradeCheckup checkup,
        string? ticker,
        string? portfolio,
        string? status)
    {
        return new PaperTradeHistoryRow(
            checkup.Id,
            checkup.TradeId,
            string.IsNullOrWhiteSpace(ticker) ? "-" : ticker,
            string.IsNullOrWhiteSpace(portfolio) ? "-" : FormatPortfolio(portfolio.Trim().ToLowerInvariant()),
            string.IsNullOrWhiteSpace(status) ? "open" : status,
            checkup.CheckupDate,
            checkup.CurrentPrice,
            checkup.UnrealizedPnl,
            checkup.PnlPct,
            checkup.DaysHeld,
            checkup.ThesisStillValid,
            checkup.ConfidenceCurrent,
            checkup.Recommendation,
            checkup.Notes);
    }

    private static decimal? CalculatePnlPct(PaperTrade trade)
    {
        if (trade.RealizedPnl is null)
        {
            return null;
        }

        var costBasis = CostBasis(trade);
        return costBasis == 0m ? null : trade.RealizedPnl.Value / costBasis * 100m;
    }

    private static decimal CostBasis(PaperTrade trade)
    {
        return Math.Abs(trade.EntryPrice * trade.Quantity);
    }

    private static bool IsOpen(PaperTrade trade)
    {
        return string.IsNullOrWhiteSpace(trade.Status) ||
               trade.Status.Equals("open", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePortfolio(PaperTrade trade)
    {
        return string.IsNullOrWhiteSpace(trade.Portfolio)
            ? "manual"
            : trade.Portfolio.Trim().ToLowerInvariant();
    }

    private static string FormatPortfolio(string portfolio)
    {
        return portfolio.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "Auto"
            : portfolio.Equals("manual", StringComparison.OrdinalIgnoreCase)
                ? "Manual"
                : portfolio;
    }
}
