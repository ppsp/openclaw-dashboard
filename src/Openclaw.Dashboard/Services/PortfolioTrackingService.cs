using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Portfolio;
using Openclaw.Dashboard.Data.Portfolio.Entities;

namespace Openclaw.Dashboard.Services;

public sealed class PortfolioTrackingService(
    IDbContextFactory<PortfolioDbContext> portfolioDbFactory,
    ILogger<PortfolioTrackingService> logger)
{
    private static readonly PortfolioAccountDefinition[] Accounts =
    [
        new("margin", "Margin"),
        new("tfsa", "TFSA"),
        new("rrsp", "RRSP")
    ];

    public async Task<PortfolioDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await portfolioDbFactory.CreateDbContextAsync(cancellationToken);
            var holdings = await db.LivePortfolio
                .AsNoTracking()
                .OrderBy(holding => holding.AccountId)
                .ThenBy(holding => holding.Ticker)
                .ToListAsync(cancellationToken);

            var transactions = await db.TransactionHistory
                .AsNoTracking()
                .OrderByDescending(transaction => transaction.InsertDate)
                .ThenByDescending(transaction => transaction.Id)
                .Take(500)
                .ToListAsync(cancellationToken);

            return BuildDashboard(holdings, transactions);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Portfolio dashboard lookup failed.");
            return EmptyDashboard();
        }
    }

    private static PortfolioDashboard BuildDashboard(
        IReadOnlyList<LivePortfolioHolding> holdings,
        IReadOnlyList<PortfolioTransaction> transactions)
    {
        var holdingRows = holdings
            .Where(holding => !string.IsNullOrWhiteSpace(holding.Ticker))
            .Select(ToHoldingRow)
            .OrderBy(row => AccountSort(row.AccountId))
            .ThenBy(row => row.Ticker)
            .ToList();

        var accountTotals = holdingRows
            .GroupBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => row.BookValue ?? 0m),
                StringComparer.OrdinalIgnoreCase);

        holdingRows = holdingRows
            .Select(row => row with
            {
                AccountWeightPct = row.BookValue is not null &&
                                   accountTotals.TryGetValue(row.AccountId, out var total) &&
                                   total > 0m
                    ? row.BookValue.Value / total * 100m
                    : null
            })
            .ToList();

        var transactionRows = transactions
            .Where(transaction => !string.IsNullOrWhiteSpace(transaction.Ticker))
            .Select(ToTransactionRow)
            .OrderByDescending(row => row.Date ?? DateTime.MinValue)
            .ThenByDescending(row => row.Id)
            .ToList();

        var accountSummaries = Accounts
            .Select(account => BuildAccountSummary(account, holdingRows))
            .ToList();

        var watchItems = BuildWatchItems(accountSummaries, holdingRows);
        var accountAllocation = accountSummaries
            .Where(account => account.BookValue is > 0m)
            .Select(account => new PortfolioChartPoint(account.AccountName, DecimalToDouble(account.BookValue!.Value)))
            .ToList();
        var holdingAllocation = BuildHoldingAllocation(holdingRows);
        var monthlyActivity = BuildMonthlyActivity(transactionRows);
        var latestUpdate = holdingRows
            .Select(row => row.LastUpdate)
            .Where(date => date.HasValue)
            .Max();

        return new PortfolioDashboard(
            accountSummaries,
            holdingRows,
            transactionRows,
            watchItems,
            accountAllocation,
            holdingAllocation,
            monthlyActivity,
            latestUpdate);
    }

    private static PortfolioAccountSummary BuildAccountSummary(
        PortfolioAccountDefinition account,
        IReadOnlyList<PortfolioHoldingRow> rows)
    {
        var accountRows = rows
            .Where(row => row.AccountId.Equals(account.AccountId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var bookValue = accountRows.Sum(row => row.BookValue ?? 0m);
        var cashLikeValue = accountRows
            .Where(row => row.IsCashLike)
            .Sum(row => row.BookValue ?? 0m);

        return new PortfolioAccountSummary(
            account.AccountId,
            account.AccountName,
            accountRows.Count,
            bookValue > 0m ? bookValue : null,
            accountRows.Count(row => row.MissingCostBasis),
            cashLikeValue > 0m ? cashLikeValue : null,
            bookValue > 0m && cashLikeValue > 0m ? cashLikeValue / bookValue * 100m : null,
            accountRows
                .Select(row => row.LastUpdate)
                .Where(date => date.HasValue)
                .Max(),
            accountRows.Count > 0);
    }

    private static PortfolioHoldingRow ToHoldingRow(LivePortfolioHolding holding)
    {
        var accountId = NormalizeAccountId(holding.AccountId);
        var avgPriceCad = PositiveOrNull(holding.AvgPriceCad);
        var avgPriceUsd = PositiveOrNull(holding.AvgPriceUsd);
        decimal? bookValue = avgPriceCad is not null
            ? avgPriceCad.Value * holding.NetAmount
            : avgPriceUsd is not null
                ? avgPriceUsd.Value * holding.NetAmount
                : null;

        return new PortfolioHoldingRow(
            accountId,
            FormatAccountName(accountId),
            holding.Ticker.Trim().ToUpperInvariant(),
            holding.NetAmount,
            avgPriceCad,
            avgPriceUsd,
            bookValue,
            avgPriceCad is not null ? "CAD" : avgPriceUsd is not null ? "USD estimate" : "Unknown",
            null,
            ParseDate(holding.LastUpdate),
            bookValue is null && holding.NetAmount != 0m,
            IsCashLike(holding.Ticker),
            null,
            null);
    }

    private static PortfolioTransactionRow ToTransactionRow(PortfolioTransaction transaction)
    {
        var accountId = NormalizeAccountId(transaction.AccountId);
        return new PortfolioTransactionRow(
            transaction.Id,
            accountId,
            FormatAccountName(accountId),
            ParseDate(transaction.InsertDate),
            transaction.Ticker.Trim().ToUpperInvariant(),
            transaction.Amount,
            PositiveOrNull(transaction.PriceCad),
            PositiveOrNull(transaction.PriceUsd),
            transaction.Amount < 0m ? "Sell / reduction" : "Buy / addition",
            transaction.Comment);
    }

    private static IReadOnlyList<PortfolioWatchItem> BuildWatchItems(
        IReadOnlyList<PortfolioAccountSummary> accounts,
        IReadOnlyList<PortfolioHoldingRow> holdings)
    {
        var items = new List<PortfolioWatchItem>();

        items.AddRange(accounts
            .Where(account => !account.HasData)
            .Select(account => new PortfolioWatchItem(
                "info",
                $"{account.AccountName} has no rows yet",
                "The account is ready on the dashboard and will populate when portfolio.db receives holdings.",
                account.AccountName,
                null)));

        items.AddRange(holdings
            .Where(row => row.MissingCostBasis)
            .Take(8)
            .Select(row => new PortfolioWatchItem(
                "warning",
                "Missing cost basis",
                "Book value cannot be calculated until avgPriceCAD or avgPriceUSD is populated.",
                row.AccountName,
                row.Ticker)));

        items.AddRange(holdings
            .Where(row => row.LastUpdate is not null && row.LastUpdate.Value < DateTime.Today.AddDays(-45))
            .Take(8)
            .Select(row => new PortfolioWatchItem(
                "warning",
                "Stale holding row",
                $"Last updated {row.LastUpdate:yyyy-MM-dd}.",
                row.AccountName,
                row.Ticker)));

        items.AddRange(holdings
            .Where(row => row.AccountWeightPct is > 20m && !row.IsCashLike)
            .OrderByDescending(row => row.AccountWeightPct)
            .Take(8)
            .Select(row => new PortfolioWatchItem(
                "risk",
                "Concentrated holding",
                $"{row.AccountWeightPct:0.0}% of {row.AccountName} book value.",
                row.AccountName,
                row.Ticker)));

        foreach (var account in accounts.Where(account => account.HasData && (account.CashLikePct is null or < 10m)))
        {
            items.Add(new PortfolioWatchItem(
                "info",
                "Cash reserve below framework target",
                "Trading framework prefers 10-20% cash for black swans and dips. This is an estimate from cash-like tickers only.",
                account.AccountName,
                null));
        }

        return items
            .OrderBy(item => item.Severity switch
            {
                "risk" => 0,
                "warning" => 1,
                _ => 2
            })
            .ThenBy(item => item.AccountName)
            .ThenBy(item => item.Ticker)
            .ToList();
    }

    private static IReadOnlyList<PortfolioChartPoint> BuildHoldingAllocation(IReadOnlyList<PortfolioHoldingRow> holdings)
    {
        var valuedHoldings = holdings
            .Where(row => row.BookValue is > 0m)
            .GroupBy(row => row.Ticker)
            .Select(group => new PortfolioChartPoint(group.Key, DecimalToDouble(group.Sum(row => row.BookValue ?? 0m))))
            .OrderByDescending(point => point.Value)
            .ToList();

        var top = valuedHoldings.Take(8).ToList();
        var otherValue = valuedHoldings.Skip(8).Sum(point => point.Value);
        if (otherValue > 0d)
        {
            top.Add(new PortfolioChartPoint("Other", otherValue));
        }

        return top;
    }

    private static IReadOnlyList<PortfolioMonthlyActivity> BuildMonthlyActivity(IReadOnlyList<PortfolioTransactionRow> transactions)
    {
        return transactions
            .Where(transaction => transaction.Date.HasValue)
            .GroupBy(transaction => new DateTime(transaction.Date!.Value.Year, transaction.Date.Value.Month, 1))
            .OrderBy(group => group.Key)
            .Select(group => new PortfolioMonthlyActivity(
                group.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                DecimalToDouble(group.Where(transaction => transaction.Amount > 0m).Sum(EstimatedTransactionValue)),
                DecimalToDouble(Math.Abs(group.Where(transaction => transaction.Amount < 0m).Sum(EstimatedTransactionValue)))))
            .ToList();
    }

    private static decimal EstimatedTransactionValue(PortfolioTransactionRow transaction)
    {
        var price = transaction.PriceCad ?? transaction.PriceUsd;
        return price is null ? 0m : transaction.Amount * price.Value;
    }

    private static PortfolioDashboard EmptyDashboard()
    {
        var accounts = Accounts
            .Select(account => new PortfolioAccountSummary(account.AccountId, account.AccountName, 0, null, 0, null, null, null, false))
            .ToList();

        return new PortfolioDashboard(accounts, [], [], [], [], [], [], null);
    }

    private static decimal? PositiveOrNull(decimal? value)
    {
        return value is > 0m ? value : null;
    }

    private static string NormalizeAccountId(string? accountId)
    {
        var normalized = string.IsNullOrWhiteSpace(accountId)
            ? "margin"
            : accountId.Trim().ToLowerInvariant();

        return normalized switch
        {
            "tfsa" => "tfsa",
            "rrsp" => "rrsp",
            "margin" => "margin",
            _ => normalized
        };
    }

    private static string FormatAccountName(string accountId)
    {
        return accountId.ToLowerInvariant() switch
        {
            "tfsa" => "TFSA",
            "rrsp" => "RRSP",
            "margin" => "Margin",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(accountId)
        };
    }

    private static int AccountSort(string accountId)
    {
        return accountId.ToLowerInvariant() switch
        {
            "margin" => 0,
            "tfsa" => 1,
            "rrsp" => 2,
            _ => 99
        };
    }

    private static bool IsCashLike(string ticker)
    {
        var normalized = ticker.Trim().ToUpperInvariant();
        return normalized is "CASH" or "CASH.CA" or "CASH.TO" or "PSA.TO" or "HISA.TO" ||
               normalized.StartsWith("CASH.", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ParseDate(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
            ? date
            : null;
    }

    private static double DecimalToDouble(decimal value)
    {
        return decimal.ToDouble(decimal.Round(value, 2));
    }

    private sealed record PortfolioAccountDefinition(string AccountId, string AccountName);
}
