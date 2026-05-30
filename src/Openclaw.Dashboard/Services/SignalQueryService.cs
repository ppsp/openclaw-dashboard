using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Openclaw.Dashboard.Data.Signals;
using Openclaw.Dashboard.Data.Signals.Entities;

namespace Openclaw.Dashboard.Services;

public sealed class SignalQueryService(
    IDbContextFactory<SignalsDbContext> signalsDbFactory,
    ILogger<SignalQueryService> logger)
{
    private static readonly Regex TickerRegex = new(@"\$?(?<ticker>[A-Z]{1,5})(?:\b|:)", RegexOptions.Compiled);

    public async Task<PagedResult<SignalRow>> SearchAsync(
        SignalFilters filters,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(0, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        try
        {
            await using var db = await signalsDbFactory.CreateDbContextAsync(cancellationToken);
            var query = ApplyFilters(db.Signals.AsNoTracking(), filters);
            var totalItems = await query.CountAsync(cancellationToken);
            var signals = await query
                .OrderByDescending(signal => signal.Id)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(signal => new
                {
                    signal.Id,
                    signal.RawSignal,
                    signal.Tier2Result,
                    signal.Source,
                    signal.DiscoveredAt,
                    signal.Tier1Score,
                    signal.Tier1Pass,
                    signal.OutcomeStatus,
                    signal.Rating
                })
                .ToListAsync(cancellationToken);

            var rows = signals
                .Select(signal => new SignalRow(
                    signal.Id,
                    ExtractTicker(signal.Tier2Result, signal.RawSignal),
                    signal.Source,
                    signal.DiscoveredAt,
                    signal.Tier1Score,
                    signal.Tier1Pass,
                    signal.OutcomeStatus,
                    signal.Rating))
                .ToList();

            return new PagedResult<SignalRow>(rows, totalItems);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Signal search failed.");
            return new PagedResult<SignalRow>([], 0);
        }
    }

    public async Task<SignalDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await signalsDbFactory.CreateDbContextAsync(cancellationToken);
            var signal = await db.Signals
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (signal is null)
            {
                return null;
            }

            return new SignalDetailDto(
                signal.Id,
                ExtractTicker(signal.Tier2Result, signal.RawSignal),
                signal.Source,
                signal.Url,
                signal.DiscoveredAt,
                signal.Status,
                signal.Tier1Score,
                signal.Tier1Pass,
                signal.Rating,
                signal.OutcomeStatus,
                signal.TriggeredAt,
                signal.ResolvedAt,
                signal.OutcomeNote,
                signal.MonitoringStart,
                signal.TtlDays,
                signal.RawSignal,
                PrettyJson(signal.Tier1Dims),
                PrettyJson(signal.Tier2Result));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Signal detail lookup failed for signal {SignalId}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await signalsDbFactory.CreateDbContextAsync(cancellationToken);

            return await db.Signals
                .AsNoTracking()
                .Where(signal => signal.Source != "")
                .Select(signal => signal.Source)
                .Distinct()
                .OrderBy(source => source)
                .Take(200)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "Source filter lookup failed.");
            return [];
        }
    }

    private static IQueryable<Signal> ApplyFilters(IQueryable<Signal> query, SignalFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Source))
        {
            query = query.Where(signal => signal.Source == filters.Source);
        }

        if (!string.IsNullOrWhiteSpace(filters.OutcomeStatus))
        {
            query = query.Where(signal => signal.OutcomeStatus == filters.OutcomeStatus);
        }

        if (filters.Tier1Pass is not null)
        {
            query = query.Where(signal => signal.Tier1Pass == filters.Tier1Pass);
        }

        if (filters.FromDate is not null)
        {
            query = query.Where(signal => signal.DiscoveredAt >= filters.FromDate.Value.Date);
        }

        if (filters.ToDate is not null)
        {
            query = query.Where(signal => signal.DiscoveredAt < filters.ToDate.Value.Date.AddDays(1));
        }

        if (!string.IsNullOrWhiteSpace(filters.Ticker))
        {
            var ticker = filters.Ticker.Trim().ToUpperInvariant();
            var symbol = $"%{ticker}%";
            query = query.Where(signal =>
                EF.Functions.Like(signal.RawSignal, symbol) ||
                (signal.Tier2Result != null && EF.Functions.Like(signal.Tier2Result, symbol)));
        }

        return query;
    }

    private static string PrettyJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    private static string ExtractTicker(string? tier2Result, string? rawSignal)
    {
        foreach (var candidate in ReadTickerCandidates(tier2Result))
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.ToUpperInvariant();
            }
        }

        var match = TickerRegex.Match(rawSignal ?? string.Empty);
        return match.Success ? match.Groups["ticker"].Value.ToUpperInvariant() : "-";
    }

    private static IEnumerable<string?> ReadTickerCandidates(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            yield break;
        }

        using var document = TryParseJson(rawJson);
        if (document is null)
        {
            yield break;
        }

        foreach (var propertyName in new[] { "ticker", "symbol", "underlying", "asset" })
        {
            if (document.RootElement.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                yield return value.GetString();
            }
        }
    }

    private static JsonDocument? TryParseJson(string rawJson)
    {
        try
        {
            return JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
