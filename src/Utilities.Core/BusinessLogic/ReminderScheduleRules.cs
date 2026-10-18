namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.Models;

public static class ReminderScheduleRules
{
    public static DateTimeOffset GetNextConfiguredAt(int dayOfMonth, TimeSpan time, int? repeatIntervalDays,
        string timeZoneId, DateTimeOffset now, ReminderSettings? existing = null)
    {
        if (dayOfMonth is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(dayOfMonth));
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(time));
        if (repeatIntervalDays is < 0) throw new ArgumentOutOfRangeException(nameof(repeatIntervalDays));

        var zone = GetTimeZone(timeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var keepAnchor = existing?.NextReminderAt is { } savedAt &&
            existing.CalendarDayOfMonth == dayOfMonth && existing.RepeatIntervalDays == repeatIntervalDays;
        var anchor = keepAnchor
            ? TimeZoneInfo.ConvertTime(existing!.NextReminderAt!.Value, zone).DateTime
            : nowLocal;

        DateTime candidate;
        if (repeatIntervalDays is null or 0 or 90 or 365)
        {
            var monthStep = repeatIntervalDays switch { 0 => 1, 90 => 3, 365 => 12, _ => 1 };
            var firstMonth = keepAnchor ? new DateTime(anchor.Year, anchor.Month, 1) : new DateTime(nowLocal.Year, nowLocal.Month, 1);
            var occurrence = 0;
            while (true)
            {
                var month = firstMonth.AddMonths(occurrence * monthStep);
                var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(month.Year, month.Month));
                candidate = DateTime.SpecifyKind(month.AddDays(day - 1).Date + time, DateTimeKind.Unspecified);
                if (candidate > nowLocal) break;
                if (repeatIntervalDays is null && keepAnchor)
                {
                    // A one-time reminder is expired; a save schedules the next selected calendar day.
                    month = new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(1);
                    day = Math.Min(dayOfMonth, DateTime.DaysInMonth(month.Year, month.Month));
                    candidate = DateTime.SpecifyKind(month.AddDays(day - 1).Date + time, DateTimeKind.Unspecified);
                    break;
                }
                occurrence++;
            }
        }
        else
        {
            var interval = repeatIntervalDays.Value;
            var firstDate = keepAnchor ? anchor.Date : nowLocal.Date;
            var firstDay = Math.Min(dayOfMonth, DateTime.DaysInMonth(firstDate.Year, firstDate.Month));
            candidate = DateTime.SpecifyKind(new DateTime(firstDate.Year, firstDate.Month, firstDay).Date + time, DateTimeKind.Unspecified);
            if (keepAnchor)
            {
                var anchorDay = Math.Min(dayOfMonth, DateTime.DaysInMonth(anchor.Year, anchor.Month));
                candidate = DateTime.SpecifyKind(new DateTime(anchor.Year, anchor.Month, anchorDay).Date + time, DateTimeKind.Unspecified);
            }
            if (!keepAnchor && candidate <= nowLocal) candidate = candidate.AddDays(interval);
            while (candidate <= nowLocal) candidate = candidate.AddDays(interval);
        }

        while (zone.IsInvalidTime(candidate)) candidate = candidate.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(candidate) ? zone.GetAmbiguousTimeOffsets(candidate).Max() : zone.GetUtcOffset(candidate);
        return new DateTimeOffset(candidate, offset);
    }

    private static TimeZoneInfo GetTimeZone(string timeZoneId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
