using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Data.Dashboard;
using Openclaw.Dashboard.Data.Dashboard.Entities;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class TickerWatchlistService(
    IDbContextFactory<DashboardDbContext> dashboardDbFactory,
    IOptions<OpenclawPathsOptions> pathsOptions,
    ILogger<TickerWatchlistService> logger)
{
    private const string SeedSourceKey = "workspace-data-watchlist-md-v1";
    private const string SeedReason = "Seeded from WATCHLIST.md";
    private static readonly Regex WatchlistLineRegex = new(
        @"^\s*[-*]\s*(?<symbol>[A-Za-z0-9.^_-]+)(?:\s*\((?<description>[^)]*)\))?\s*$",
        RegexOptions.Compiled);

    private readonly OpenclawPathsOptions _paths = pathsOptions.Value;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
    }

    public async Task<int> SeedFromWorkspaceWatchlistAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var seeded = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ticker_watchlist_seed_state WHERE SourceKey = {0}", SeedSourceKey)
            .SingleAsync(cancellationToken);
        var existingCount = await db.TickerWatchlistItems.CountAsync(cancellationToken);
        if (seeded > 0 && existingCount > 0)
        {
            return 0;
        }

        if (seeded == 0 && existingCount > 0)
        {
            await MarkSeededAsync(db, cancellationToken);
            return 0;
        }

        var items = await ReadSeedItemsAsync(cancellationToken);
        if (items.Count == 0)
        {
            await MarkSeededAsync(db, cancellationToken);
            return 0;
        }

        db.TickerWatchlistItems.AddRange(items);
        await MarkSeededAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    public async Task<IReadOnlyList<TickerWatchlistRow>> GetRowsAsync(
        string? assetClass,
        string? search,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
        var query = db.TickerWatchlistItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(assetClass))
        {
            var normalizedClass = NormalizeAssetClass(assetClass);
            query = query.Where(item => item.AssetClass == normalizedClass);
        }

        var items = await query
            .OrderBy(item => item.AssetClass)
            .ThenBy(item => item.Symbol)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            items = items
                .Where(item =>
                    Contains(item.Symbol, term) ||
                    Contains(item.Sector, term) ||
                    Contains(item.Description, term) ||
                    Contains(item.WatchReason, term))
                .ToList();
        }

        return items.Select(ToRow).ToList();
    }

    public async Task<TickerWatchlistEditModel?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
        var item = await db.TickerWatchlistItems.FindAsync([id], cancellationToken);
        return item is null ? null : ToEditModel(item);
    }

    public async Task SaveAsync(TickerWatchlistEditModel model, CancellationToken cancellationToken = default)
    {
        Normalize(model);
        Validate(model);

        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var duplicateExists = await db.TickerWatchlistItems
            .AnyAsync(item => item.Symbol == model.Symbol && item.Id != model.Id, cancellationToken);
        if (duplicateExists)
        {
            throw new InvalidOperationException($"Symbol '{model.Symbol}' is already in the ticker watchlist.");
        }

        TickerWatchlistItem entity;
        if (model.Id == 0)
        {
            entity = new TickerWatchlistItem
            {
                CreatedAt = DateTime.UtcNow
            };
            db.TickerWatchlistItems.Add(entity);
        }
        else
        {
            entity = await db.TickerWatchlistItems.FindAsync([model.Id], cancellationToken)
                ?? throw new InvalidOperationException("Ticker watchlist item not found.");
        }

        entity.Symbol = model.Symbol;
        entity.AssetClass = model.AssetClass;
        entity.Sector = EmptyToNull(model.Sector);
        entity.Description = EmptyToNull(model.Description);
        entity.WatchReason = EmptyToNull(model.WatchReason);
        entity.Status = model.Status;
        entity.Conviction = model.Conviction;
        entity.TimeHorizon = EmptyToNull(model.TimeHorizon);
        entity.Notes = EmptyToNull(model.Notes);
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dashboardDbFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
        var entity = await db.TickerWatchlistItems.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return;
        }

        db.TickerWatchlistItems.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureSchemaAsync(DashboardDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ticker_watchlist_items (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol TEXT NOT NULL,
                AssetClass TEXT NOT NULL,
                Sector TEXT NULL,
                Description TEXT NULL,
                WatchReason TEXT NULL,
                Status TEXT NOT NULL DEFAULT 'active',
                Conviction INTEGER NULL,
                TimeHorizon TEXT NULL,
                Notes TEXT NULL,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ticker_watchlist_items_Symbol ON ticker_watchlist_items (Symbol);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ticker_watchlist_items_AssetClass ON ticker_watchlist_items (AssetClass);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ticker_watchlist_items_Status ON ticker_watchlist_items (Status);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ticker_watchlist_seed_state (
                SourceKey TEXT NOT NULL PRIMARY KEY,
                SeededAt TEXT NOT NULL
            );
            """,
            cancellationToken);
    }

    private async Task<IReadOnlyList<TickerWatchlistItem>> ReadSeedItemsAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.WorkspacePath, "data", "WATCHLIST.md");
        if (!File.Exists(path))
        {
            logger.LogWarning("Ticker watchlist seed file was not found at {Path}.", path);
            return [];
        }

        try
        {
            var rawText = await File.ReadAllTextAsync(path, cancellationToken);
            var lines = rawText.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Split(["\r\n", "\n"], StringSplitOptions.None);
            var assetClass = string.Empty;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<TickerWatchlistItem>();

            foreach (var line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    assetClass = line.Trim().ToLowerInvariant() switch
                    {
                        "## etfs" => "etf",
                        "## stocks" => "stock",
                        "## crypto" => "crypto",
                        _ => string.Empty
                    };
                    continue;
                }

                if (string.IsNullOrWhiteSpace(assetClass))
                {
                    continue;
                }

                var match = WatchlistLineRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var symbol = NormalizeSymbol(match.Groups["symbol"].Value);
                if (string.IsNullOrWhiteSpace(symbol) || !seen.Add(symbol))
                {
                    continue;
                }

                items.Add(new TickerWatchlistItem
                {
                    Symbol = symbol,
                    AssetClass = assetClass,
                    Description = EmptyToNull(match.Groups["description"].Value),
                    WatchReason = SeedReason,
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            return items;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read ticker watchlist seed file.");
            return [];
        }
    }

    private static async Task MarkSeededAsync(DashboardDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT OR IGNORE INTO ticker_watchlist_seed_state (SourceKey, SeededAt)
            VALUES ({0}, {1});
            """,
            [SeedSourceKey, DateTime.UtcNow.ToString("O")],
            cancellationToken);
    }

    private static void Normalize(TickerWatchlistEditModel model)
    {
        model.Symbol = NormalizeSymbol(model.Symbol);
        model.AssetClass = NormalizeAssetClass(model.AssetClass);
        model.Status = NormalizeStatus(model.Status);
        model.Sector = model.Sector?.Trim();
        model.Description = model.Description?.Trim();
        model.WatchReason = model.WatchReason?.Trim();
        model.TimeHorizon = model.TimeHorizon?.Trim();
        model.Notes = model.Notes?.Trim();
    }

    private static void Validate(TickerWatchlistEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Symbol))
        {
            throw new InvalidOperationException("Symbol is required.");
        }

        if (!IsKnownAssetClass(model.AssetClass))
        {
            throw new InvalidOperationException("Asset class must be stock, crypto, or etf.");
        }

        if (!IsKnownStatus(model.Status))
        {
            throw new InvalidOperationException("Status must be active, paused, or archived.");
        }

        if (model.Conviction is < 1 or > 5)
        {
            throw new InvalidOperationException("Conviction must be between 1 and 5.");
        }
    }

    private static string NormalizeSymbol(string? symbol)
    {
        return string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().TrimStart('$').ToUpperInvariant();
    }

    private static string NormalizeAssetClass(string? assetClass)
    {
        var normalized = assetClass?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "stocks" => "stock",
            "cryptos" => "crypto",
            "etfs" => "etf",
            _ => normalized
        };
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "active" : status.Trim().ToLowerInvariant();
    }

    private static bool IsKnownAssetClass(string assetClass)
    {
        return assetClass is "stock" or "crypto" or "etf";
    }

    private static bool IsKnownStatus(string status)
    {
        return status is "active" or "paused" or "archived";
    }

    private static bool Contains(string? value, string term)
    {
        return value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TickerWatchlistRow ToRow(TickerWatchlistItem item)
    {
        return new TickerWatchlistRow(
            item.Id,
            item.Symbol,
            item.AssetClass,
            item.Sector,
            item.Description,
            item.WatchReason,
            item.Status,
            item.Conviction,
            item.TimeHorizon,
            item.Notes,
            item.CreatedAt,
            item.UpdatedAt);
    }

    private static TickerWatchlistEditModel ToEditModel(TickerWatchlistItem item)
    {
        return new TickerWatchlistEditModel
        {
            Id = item.Id,
            Symbol = item.Symbol,
            AssetClass = item.AssetClass,
            Sector = item.Sector,
            Description = item.Description,
            WatchReason = item.WatchReason,
            Status = item.Status,
            Conviction = item.Conviction,
            TimeHorizon = item.TimeHorizon,
            Notes = item.Notes
        };
    }
}
