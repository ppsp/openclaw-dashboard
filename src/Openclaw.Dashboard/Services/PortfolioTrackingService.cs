using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Portfolio;
using Openclaw.Dashboard.Data.Portfolio.Entities;

namespace Openclaw.Dashboard.Services;

public sealed class PortfolioTrackingService(
    IDbContextFactory<PortfolioDbContext> portfolioDbFactory,
    MarketPriceService marketPriceService,
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

            var quotes = await marketPriceService.GetCachedPricesAsync(
                holdings.Select(holding => holding.Ticker),
                cancellationToken);

            return BuildDashboard(holdings, transactions, quotes);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Portfolio dashboard lookup failed.");
            return EmptyDashboard();
        }
    }

    private static PortfolioDashboard BuildDashboard(
        IReadOnlyList<LivePortfolioHolding> holdings,
        IReadOnlyList<PortfolioTransaction> transactions,
        IReadOnlyDictionary<string, MarketPriceQuote> quotes)
    {
        var holdingRows = holdings
            .Where(holding => !string.IsNullOrWhiteSpace(holding.Ticker))
            .Select(holding => ToHoldingRow(holding, quotes))
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
        var latestPriceFetchedAt = holdingRows
            .Select(row => row.CurrentPriceFetchedAt)
            .Where(date => date.HasValue)
            .Max();
        var totalCurrentCurrency = SingleCurrency(holdingRows);
        var totalCurrentValue = totalCurrentCurrency is null
            ? null
            : SumPositiveOrNull(holdingRows.Select(row => row.CurrentValue));
        var totalUnrealizedPnl = totalCurrentCurrency is null
            ? null
            : SumOrNull(holdingRows.Select(row => row.UnrealizedPnl));
        var totalBookValueForPnl = totalCurrentCurrency is null
            ? null
            : SumPositiveOrNull(holdingRows.Where(row => row.UnrealizedPnl is not null).Select(row => row.BookValue));

        return new PortfolioDashboard(
            accountSummaries,
            holdingRows,
            transactionRows,
            watchItems,
            accountAllocation,
            holdingAllocation,
            monthlyActivity,
            latestUpdate,
            totalCurrentValue,
            totalUnrealizedPnl,
            totalUnrealizedPnl is not null && totalBookValueForPnl is > 0m ? totalUnrealizedPnl / totalBookValueForPnl * 100m : null,
            totalCurrentCurrency,
            latestPriceFetchedAt);
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
        var currentCurrency = SingleCurrency(accountRows);
        var currentValue = currentCurrency is null
            ? null
            : SumPositiveOrNull(accountRows.Select(row => row.CurrentValue));
        var unrealizedPnl = currentCurrency is null
            ? null
            : SumOrNull(accountRows.Select(row => row.UnrealizedPnl));
        var bookValueForPnl = currentCurrency is null
            ? null
            : SumPositiveOrNull(accountRows.Where(row => row.UnrealizedPnl is not null).Select(row => row.BookValue));

        return new PortfolioAccountSummary(
            account.AccountId,
            account.AccountName,
            accountRows.Count,
            bookValue > 0m ? bookValue : null,
            accountRows.Count(row => row.MissingCostBasis),
            cashLikeValue > 0m ? cashLikeValue : null,
            bookValue > 0m && cashLikeValue > 0m ? cashLikeValue / bookValue * 100m : null,
            currentValue,
            unrealizedPnl,
            unrealizedPnl is not null && bookValueForPnl is > 0m ? unrealizedPnl / bookValueForPnl * 100m : null,
            currentCurrency,
            accountRows
                .Select(row => row.LastUpdate)
                .Where(date => date.HasValue)
                .Max(),
            accountRows
                .Select(row => row.CurrentPriceFetchedAt)
                .Where(date => date.HasValue)
                .Max(),
            accountRows.Count > 0);
    }

    private static PortfolioHoldingRow ToHoldingRow(
        LivePortfolioHolding holding,
        IReadOnlyDictionary<string, MarketPriceQuote> quotes)
    {
        var accountId = NormalizeAccountId(holding.AccountId);
        var ticker = MarketPriceService.NormalizeSymbol(holding.Ticker);
        var avgPriceCad = PositiveOrNull(holding.AvgPriceCad);
        var avgPriceUsd = PositiveOrNull(holding.AvgPriceUsd);
        decimal? bookValue = avgPriceCad is not null
            ? avgPriceCad.Value * holding.NetAmount
            : avgPriceUsd is not null
                ? avgPriceUsd.Value * holding.NetAmount
                : null;
        quotes.TryGetValue(ticker, out var quote);
        var currentPrice = PositiveOrNull(quote?.Price);
        decimal? currentValue = currentPrice is null ? null : currentPrice.Value * holding.NetAmount;
        var basisCurrency = avgPriceCad is not null ? "CAD" : avgPriceUsd is not null ? "USD" : null;
        var quoteCurrency = string.IsNullOrWhiteSpace(quote?.Currency) ? null : quote.Currency;
        decimal? unrealizedPnl = currentValue is not null &&
                                 bookValue is not null &&
                                 quoteCurrency is not null &&
                                 basisCurrency is not null &&
                                 quoteCurrency.Equals(basisCurrency, StringComparison.OrdinalIgnoreCase)
            ? currentValue - bookValue
            : null;

        return new PortfolioHoldingRow(
            accountId,
            FormatAccountName(accountId),
            ticker,
            holding.NetAmount,
            avgPriceCad,
            avgPriceUsd,
            bookValue,
            avgPriceCad is not null ? "CAD" : avgPriceUsd is not null ? "USD estimate" : "Unknown",
            null,
            ParseDate(holding.LastUpdate),
            bookValue is null && holding.NetAmount != 0m,
            IsCashLike(holding.Ticker),
            currentPrice,
            quoteCurrency,
            quote?.FetchedAtUtc,
            quote?.Provider,
            quote?.LastError,
            currentValue,
            unrealizedPnl,
            unrealizedPnl is not null && bookValue is > 0m ? unrealizedPnl / bookValue * 100m : null);
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

        items.AddRange(holdings
            .Where(row => !string.IsNullOrWhiteSpace(row.CurrentPriceError))
            .Take(8)
            .Select(row => new PortfolioWatchItem(
                "warning",
                "Price refresh failed",
                row.CurrentPriceError!,
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
            .Select(account => new PortfolioAccountSummary(account.AccountId, account.AccountName, 0, null, 0, null, null, null, null, null, null, null, null, false))
            .ToList();

        return new PortfolioDashboard(accounts, [], [], [], [], [], [], null, null, null, null, null, null);
    }

    private static string? SingleCurrency(IReadOnlyList<PortfolioHoldingRow> rows)
    {
        var currencies = rows
            .Where(row => row.CurrentValue is not null && !string.IsNullOrWhiteSpace(row.CurrentPriceCurrency))
            .Select(row => row.CurrentPriceCurrency!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        return currencies.Count == 1 ? currencies[0] : null;
    }

    private static decimal? SumPositiveOrNull(IEnumerable<decimal?> values)
    {
        var total = values.Sum(value => value ?? 0m);
        return total > 0m ? total : null;
    }

    private static decimal? SumOrNull(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return known.Count == 0 ? null : known.Sum();
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
