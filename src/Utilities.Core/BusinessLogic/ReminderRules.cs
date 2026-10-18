namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.Models;

public static class ReminderRules
{
    public static bool IsDue(ReminderSettings settings, DateTimeOffset now) =>
        settings.IsEnabled && settings.NextReminderAt <= now;

    public static DateTimeOffset? GetNextOccurrence(ReminderSettings settings, DateTimeOffset now)
    {
        if (!settings.IsEnabled || !settings.NextReminderAt.HasValue) return null;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { zone = TimeZoneInfo.Utc; }
        var firstLocal = TimeZoneInfo.ConvertTime(settings.NextReminderAt.Value, zone).DateTime;
        if (settings.NextReminderAt.Value > now || !settings.RepeatIntervalDays.HasValue) return settings.NextReminderAt;
        var nowLocal = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateTime nextLocal;
        if (settings.RepeatIntervalDays is 0 or 90 or 365)
        {
            var intervalMonths = settings.RepeatIntervalDays.Value switch { 0 => 1, 90 => 3, _ => 12 };
            var intervalCount = 1;
            nextLocal = CalendarOccurrence(firstLocal, intervalMonths, intervalCount, settings.CalendarDayOfMonth);
            while (nextLocal <= nowLocal)
            {
                intervalCount++;
                nextLocal = CalendarOccurrence(firstLocal, intervalMonths, intervalCount, settings.CalendarDayOfMonth);
            }
        }
        else
        {
            var missedIntervals = Math.Floor((nowLocal - firstLocal).TotalDays / settings.RepeatIntervalDays.Value) + 1;
            nextLocal = firstLocal.AddDays(missedIntervals * settings.RepeatIntervalDays.Value);
        }
        while (zone.IsInvalidTime(nextLocal)) nextLocal = nextLocal.AddMinutes(1);
        return new DateTimeOffset(nextLocal, zone.GetUtcOffset(nextLocal));
    }

    private static DateTime CalendarOccurrence(DateTime firstLocal, int intervalMonths, int intervalCount, int? calendarDayOfMonth)
    {
        var month = new DateTime(firstLocal.Year, firstLocal.Month, 1, firstLocal.Hour, firstLocal.Minute, firstLocal.Second, firstLocal.Millisecond, DateTimeKind.Unspecified)
            .AddMonths(intervalMonths * intervalCount);
        return new DateTime(month.Year, month.Month, Math.Min(calendarDayOfMonth ?? firstLocal.Day, DateTime.DaysInMonth(month.Year, month.Month)),
            firstLocal.Hour, firstLocal.Minute, firstLocal.Second, firstLocal.Millisecond, DateTimeKind.Unspecified).AddTicks(firstLocal.Ticks % TimeSpan.TicksPerMillisecond);
    }
}
