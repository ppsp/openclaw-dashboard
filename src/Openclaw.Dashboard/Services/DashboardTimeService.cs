using System.Globalization;
using Microsoft.Extensions.Options;
using Openclaw.Dashboard.Options;

namespace Openclaw.Dashboard.Services;

public sealed class DashboardTimeService
{
    private readonly TimeZoneInfo _timeZone;

    public DashboardTimeService(IOptions<DashboardTimeOptions> options)
    {
        _timeZone = ResolveTimeZone(options.Value.TimeZoneId);
    }

    public string TimeZoneId => _timeZone.Id;

    public string FormatUtcAsDashboardTime(DateTime? value)
    {
        if (value is null)
        {
            return "-";
        }

        var local = ConvertUtcToDashboardTime(value.Value);
        var suffix = GetEasternSuffix(local);

        return $"{local.ToString("M/d/yyyy h:mm tt", CultureInfo.InvariantCulture)} {suffix}";
    }

    public DateTime ConvertUtcToDashboardTime(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);
    }

    public DateTime Today()
    {
        return ConvertUtcToDashboardTime(DateTime.UtcNow).Date;
    }

    public DateTime ConvertDashboardDateStartToUtc(DateTime value)
    {
        var localStart = DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localStart, _timeZone);
    }

    public DateTime ConvertDashboardDateEndExclusiveToUtc(DateTime value)
    {
        var localEnd = DateTime.SpecifyKind(value.Date.AddDays(1), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeZone);
    }

    private static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        foreach (var id in new[] { configuredId, "America/Nassau", "Eastern Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private string GetEasternSuffix(DateTime localTime)
    {
        if (!_timeZone.Id.Equals("Eastern Standard Time", StringComparison.OrdinalIgnoreCase) &&
            !_timeZone.Id.Equals("America/Nassau", StringComparison.OrdinalIgnoreCase) &&
            !_timeZone.Id.Equals("America/New_York", StringComparison.OrdinalIgnoreCase))
        {
            return _timeZone.IsDaylightSavingTime(localTime) ? _timeZone.DaylightName : _timeZone.StandardName;
        }

        return _timeZone.IsDaylightSavingTime(localTime) ? "EDT" : "EST";
    }
}
