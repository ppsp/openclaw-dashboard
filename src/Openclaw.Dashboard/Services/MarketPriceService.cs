using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Dashboard.Entities;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class MarketPriceService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<MarketPriceOptions> options,
    ILogger<MarketPriceService> logger)
{
    private const string YahooProvider = "Yahoo";
    private readonly MarketPriceOptions _options = options.Value;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, MarketPriceQuote>> GetCachedPricesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var normalizedSymbols = NormalizeSymbols(symbols);
        if (normalizedSymbols.Count == 0)
        {
            return new Dictionary<string, MarketPriceQuote>(StringComparer.OrdinalIgnoreCase);
        }

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var provider = NormalizeProvider(_options.Provider);
        var rows = await db.MarketPriceCaches
            .AsNoTracking()
            .Where(row => row.Provider == provider && normalizedSymbols.Contains(row.Symbol))
            .ToListAsync(cancellationToken);

        return rows
            .Select(ToQuote)
            .ToDictionary(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<MarketPriceRefreshResult> RefreshStalePricesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var normalizedSymbols = NormalizeSymbols(symbols);
        if (normalizedSymbols.Count == 0)
        {
            return new MarketPriceRefreshResult(0, 0, 0, DateTime.UtcNow);
        }

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var provider = NormalizeProvider(_options.Provider);
        var now = DateTime.UtcNow;
        var staleBefore = now.AddMinutes(-Math.Max(1, _options.CacheMinutes));
        var existing = await db.MarketPriceCaches
            .Where(row => row.Provider == provider && normalizedSymbols.Contains(row.Symbol))
            .ToDictionaryAsync(row => row.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var staleSymbols = normalizedSymbols
            .Where(symbol => !existing.TryGetValue(symbol, out var cached) ||
                             cached.FetchedAtUtc is null ||
                             cached.FetchedAtUtc < staleBefore ||
                             cached.Price is null)
            .ToList();

        var refreshed = 0;
        var failed = 0;
        foreach (var symbol in staleSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = existing.TryGetValue(symbol, out var cached)
                ? cached
                : new MarketPriceCache { Symbol = symbol, Provider = provider };

            if (!existing.ContainsKey(symbol))
            {
                db.MarketPriceCaches.Add(row);
                existing[symbol] = row;
            }

            row.LastAttemptAtUtc = DateTime.UtcNow;
            try
            {
                var quote = await FetchYahooQuoteAsync(symbol, cancellationToken);
                row.Price = quote.Price;
                row.Currency = quote.Currency;
                row.PreviousClose = quote.PreviousClose;
                row.Change = quote.Change;
                row.ChangePct = quote.ChangePct;
                row.QuoteTimeUtc = quote.QuoteTimeUtc;
                row.FetchedAtUtc = DateTime.UtcNow;
                row.LastError = null;
                refreshed++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                failed++;
                row.LastError = ex.Message;
                logger.LogWarning(ex, "Market price refresh failed for {Symbol}.", symbol);
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new MarketPriceRefreshResult(normalizedSymbols.Count, refreshed, failed, DateTime.UtcNow);
    }

    private async Task<MarketPriceQuote> FetchYahooQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        if (!NormalizeProvider(_options.Provider).Equals(YahooProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported market price provider '{_options.Provider}'.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

        var client = httpClientFactory.CreateClient("MarketPrices");
        var escapedSymbol = Uri.EscapeDataString(symbol);
        using var response = await client.GetAsync(
            $"https://query1.finance.yahoo.com/v8/finance/chart/{escapedSymbol}?range=1d&interval=1m",
            timeoutCts.Token);

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

        var root = document.RootElement.GetProperty("chart");
        if (root.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null)
        {
            throw new InvalidOperationException(error.GetProperty("description").GetString() ?? "Yahoo returned an error.");
        }

        var result = root.GetProperty("result");
        if (result.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Yahoo returned no quote result.");
        }

        var meta = result[0].GetProperty("meta");
        var price = ReadDecimal(meta, "regularMarketPrice");
        var previousClose = ReadDecimal(meta, "chartPreviousClose") ?? ReadDecimal(meta, "previousClose");
        var change = price is not null && previousClose is not null ? price - previousClose : null;
        var changePct = change is not null && previousClose is > 0m ? change / previousClose * 100m : null;
        var quoteTimeUtc = ReadUnixSeconds(meta, "regularMarketTime");
        var currency = meta.TryGetProperty("currency", out var currencyElement)
            ? currencyElement.GetString()
            : null;

        if (price is null)
        {
            throw new InvalidOperationException("Yahoo quote did not include a market price.");
        }

        return new MarketPriceQuote(
            symbol,
            YahooProvider,
            price,
            string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(),
            previousClose,
            change,
            changePct,
            quoteTimeUtc,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);
    }

    private static async Task EnsureSchemaAsync(DashboardDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS market_price_cache (
                Symbol TEXT NOT NULL,
                Provider TEXT NOT NULL,
                Price TEXT NULL,
                Currency TEXT NULL,
                PreviousClose TEXT NULL,
                Change TEXT NULL,
                ChangePct TEXT NULL,
                QuoteTimeUtc TEXT NULL,
                FetchedAtUtc TEXT NULL,
                LastAttemptAtUtc TEXT NULL,
                LastError TEXT NULL,
                CONSTRAINT PK_market_price_cache PRIMARY KEY (Symbol, Provider)
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_market_price_cache_FetchedAtUtc ON market_price_cache (FetchedAtUtc);",
            cancellationToken);
    }

    public static string NormalizeSymbol(string? symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().TrimStart('$').ToUpperInvariant();
    }

    private static IReadOnlyList<string> NormalizeSymbols(IEnumerable<string> symbols)
    {
        return symbols
            .Select(NormalizeSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol)
            .ToList();
    }

    private static string NormalizeProvider(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider)
            ? YahooProvider
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(provider.Trim().ToLowerInvariant());
    }

    private static MarketPriceQuote ToQuote(MarketPriceCache cache)
    {
        return new MarketPriceQuote(
            cache.Symbol,
            cache.Provider,
            cache.Price,
            cache.Currency,
            cache.PreviousClose,
            cache.Change,
            cache.ChangePct,
            cache.QuoteTimeUtc,
            cache.FetchedAtUtc,
            cache.LastAttemptAtUtc,
            cache.LastError);
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    private static DateTime? ReadUnixSeconds(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var unixSeconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
    }
}
