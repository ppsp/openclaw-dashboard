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
    private static readonly Regex LabeledTickerRegex = new(
        @"(?:Ticker|Symbol|Underlying|Asset)\s*:\s*\$?(?<ticker>[A-Z][A-Z0-9._-]{0,9})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CashtagRegex = new(
        @"(?<![A-Za-z0-9])\$(?<ticker>[A-Z][A-Z0-9._-]{0,9})\b",
        RegexOptions.Compiled);

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
                    signal.Url,
                    signal.Tier1Dims,
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
                    ExtractTicker(signal.Tier2Result, signal.Tier1Dims, signal.RawSignal),
                    BuildDescription(signal.RawSignal, 120),
                    BuildDescription(signal.RawSignal, 500),
                    signal.Source,
                    signal.Url,
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
                ExtractTicker(signal.Tier2Result, signal.Tier1Dims, signal.RawSignal),
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
                (signal.Tier1Dims != null && EF.Functions.Like(signal.Tier1Dims, symbol)) ||
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

    private static string BuildDescription(string? rawSignal, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(rawSignal))
        {
            return "-";
        }

        var normalized = Regex.Replace(rawSignal, @"\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : $"{normalized[..(maxLength - 3)]}...";
    }

    private static string ExtractTicker(string? tier2Result, string? tier1Dims, string? rawSignal)
    {
        foreach (var candidate in ReadTickerCandidatesFromJson(tier2Result))
        {
            if (TryNormalizeTicker(candidate, out var ticker))
            {
                return ticker;
            }
        }

        foreach (var candidate in ReadTickerCandidatesFromJson(tier1Dims))
        {
            if (TryNormalizeTicker(candidate, out var ticker))
            {
                return ticker;
            }
        }

        foreach (var candidate in ReadTickerCandidatesFromText(rawSignal))
        {
            if (TryNormalizeTicker(candidate, out var ticker))
            {
                return ticker;
            }
        }

        return "-";
    }

    private static IEnumerable<string?> ReadTickerCandidatesFromText(string? rawSignal)
    {
        if (string.IsNullOrWhiteSpace(rawSignal))
        {
            yield break;
        }

        var labeledMatch = LabeledTickerRegex.Match(rawSignal);
        if (labeledMatch.Success)
        {
            yield return labeledMatch.Groups["ticker"].Value;
        }

        foreach (Match match in CashtagRegex.Matches(rawSignal))
        {
            yield return match.Groups["ticker"].Value;
        }
    }

    private static bool TryNormalizeTicker(string? candidate, out string ticker)
    {
        ticker = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim().TrimStart('$').Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > 10 || !char.IsLetter(normalized[0]))
        {
            return false;
        }

        if (normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_'))
        {
            return false;
        }

        ticker = normalized;
        return true;
    }

    private static IEnumerable<string?> ReadTickerCandidatesFromJson(string? rawJson)
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
