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
                    signal.Status,
                    signal.Tier1Route,
                    signal.AlphaScore,
                    signal.ReadinessScore,
                    signal.ReasonCategory,
                    signal.NextAction,
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
                    ReadStringFromJson(signal.Tier1Dims, "direction") ?? "-",
                    BuildDescription(signal.RawSignal, 120),
                    BuildDescription(signal.RawSignal, 500),
                    signal.Source,
                    signal.Url,
                    signal.DiscoveredAt,
                    ResolveRoute(signal.Tier1Route, signal.Tier1Dims, signal.Status),
                    signal.AlphaScore ?? ReadIntFromJson(signal.Tier1Dims, "alpha_score"),
                    signal.ReadinessScore ?? ReadIntFromJson(signal.Tier1Dims, "readiness_score"),
                    signal.ReasonCategory ?? ReadStringFromJson(signal.Tier1Dims, "reason_category") ?? "-",
                    signal.NextAction ?? ReadStringFromJson(signal.Tier1Dims, "next_action") ?? "-",
                    signal.Status,
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
                ResolveRoute(signal.Tier1Route, signal.Tier1Dims, signal.Status),
                signal.AlphaScore ?? ReadIntFromJson(signal.Tier1Dims, "alpha_score"),
                signal.ReadinessScore ?? ReadIntFromJson(signal.Tier1Dims, "readiness_score"),
                signal.ReasonCategory ?? ReadStringFromJson(signal.Tier1Dims, "reason_category") ?? "-",
                signal.NextAction ?? ReadStringFromJson(signal.Tier1Dims, "next_action") ?? "-",
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

        if (!string.IsNullOrWhiteSpace(filters.RouteView))
        {
            query = ApplyRouteFilter(query, filters.RouteView);
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

    private static IQueryable<Signal> ApplyRouteFilter(IQueryable<Signal> query, string routeView)
    {
        return routeView switch
        {
            "unrouted" => query.Where(signal =>
                signal.Status == "new" &&
                (signal.Tier1Route == null || signal.Tier1Route == "")),
            "watch" => query.Where(signal =>
                signal.Tier1Route == "watch" ||
                (signal.Tier1Route == null &&
                 signal.Tier1Dims != null &&
                 EF.Functions.Like(signal.Tier1Dims, "%\"tier1_route\"%\"watch\"%")) ||
                signal.Status == "tier1_watch"),
            "pass_pending" => query.Where(signal =>
                (signal.Tier1Route == "pass" ||
                 (signal.Tier1Route == null &&
                  signal.Tier1Dims != null &&
                  EF.Functions.Like(signal.Tier1Dims, "%\"tier1_route\"%\"pass\"%"))) &&
                (signal.Tier2Result == null || signal.Tier2Result == "")),
            "fast_track_pending" => query.Where(signal =>
                (signal.Tier1Route == "fast_track" ||
                 (signal.Tier1Route == null &&
                  signal.Tier1Dims != null &&
                  EF.Functions.Like(signal.Tier1Dims, "%\"tier1_route\"%\"fast_track\"%"))) &&
                (signal.Tier2Result == null || signal.Tier2Result == "")),
            "tier2_pending" => query.Where(signal =>
                (signal.Tier1Route == "pass" ||
                 signal.Tier1Route == "fast_track" ||
                 (signal.Tier1Route == null &&
                  signal.Tier1Dims != null &&
                  (EF.Functions.Like(signal.Tier1Dims, "%\"tier1_route\"%\"pass\"%") ||
                   EF.Functions.Like(signal.Tier1Dims, "%\"tier1_route\"%\"fast_track\"%")))) &&
                (signal.Tier2Result == null || signal.Tier2Result == "")),
            "rejected" => query.Where(signal =>
                signal.Tier1Route == "reject" ||
                (signal.Tier1Route == null &&
                 signal.Tier1Dims != null &&
                 EF.Functions.Like(signal.Tier1Dims, "%\"tier1_route\"%\"reject\"%")) ||
                signal.Status == "tier1_reject"),
            "tier2_complete" => query.Where(signal =>
                signal.Status == "tier2_complete" ||
                (signal.Tier2Result != null && signal.Tier2Result != "")),
            "active" => query.Where(signal =>
                signal.OutcomeStatus == "active" ||
                signal.OutcomeStatus == "triggered" ||
                signal.OutcomeStatus == "resolved" ||
                signal.OutcomeStatus == "invalid"),
            _ => query
        };
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

    private static string ResolveRoute(string? route, string? tier1Dims, string? status)
    {
        var resolved = route ?? ReadStringFromJson(tier1Dims, "tier1_route");

        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved.Trim().ToLowerInvariant();
        }

        return status switch
        {
            "tier1_watch" => "watch",
            "tier1_pass" => "pass",
            "tier1_reject" => "reject",
            "new" => "new",
            _ => "-"
        };
    }

    private static string? ReadStringFromJson(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        using var document = TryParseJson(rawJson);
        if (document is null ||
            !document.RootElement.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var result = value.GetString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static int? ReadIntFromJson(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        using var document = TryParseJson(rawJson);
        if (document is null ||
            !document.RootElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
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
