namespace Utilities.Core.Domain.Models;

public enum ReminderOwnerType { Meter, Subscription }
public enum ReminderKind { ReadingSubmission, Payment }

public sealed class ReminderSettings : SoftDeletableEntity
{
    private ReminderSettings() { }

    public ReminderSettings(ReminderOwnerType ownerType, Guid ownerId, ReminderKind kind, bool isEnabled, DateTimeOffset? nextReminderAt, int? repeatIntervalDays = null, string? timeZoneId = null, int? calendarDayOfMonth = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        if (repeatIntervalDays is < 0) throw new ArgumentOutOfRangeException(nameof(repeatIntervalDays));
        if (calendarDayOfMonth is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(calendarDayOfMonth));
        if (!Enum.IsDefined(ownerType)) throw new ArgumentOutOfRangeException(nameof(ownerType));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (isEnabled && !nextReminderAt.HasValue) throw new ArgumentException("An enabled reminder needs a date and time.", nameof(nextReminderAt));
        if (ownerType == ReminderOwnerType.Subscription && kind == ReminderKind.ReadingSubmission) throw new ArgumentException("Subscriptions support payment reminders only.", nameof(kind));
        OwnerType = ownerType; OwnerId = ownerId; Kind = kind; IsEnabled = isEnabled; NextReminderAt = nextReminderAt; RepeatIntervalDays = repeatIntervalDays; TimeZoneId = timeZoneId ?? TimeZoneInfo.Local.Id; CalendarDayOfMonth = calendarDayOfMonth;
    }
    public ReminderOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public ReminderKind Kind { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset? NextReminderAt { get; private set; }
    public int? RepeatIntervalDays { get; private set; }
    public string TimeZoneId { get; private set; } = TimeZoneInfo.Local.Id;
    public int? CalendarDayOfMonth { get; private set; }

    public void Configure(bool enabled, DateTimeOffset? nextReminderAt, int? repeatIntervalDays, string? timeZoneId = null, int? calendarDayOfMonth = null)
    {
        if (repeatIntervalDays is < 0) throw new ArgumentOutOfRangeException(nameof(repeatIntervalDays));
        if (calendarDayOfMonth is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(calendarDayOfMonth));
        if (enabled && !nextReminderAt.HasValue) throw new ArgumentException("An enabled reminder needs a date and time.", nameof(nextReminderAt));
        if (Kind == ReminderKind.ReadingSubmission && OwnerType == ReminderOwnerType.Subscription)
            throw new InvalidOperationException("Subscriptions support payment reminders only.");
        IsEnabled = enabled; NextReminderAt = nextReminderAt; RepeatIntervalDays = repeatIntervalDays; TimeZoneId = timeZoneId ?? TimeZoneId; CalendarDayOfMonth = calendarDayOfMonth ?? CalendarDayOfMonth; Touch();
    }
}
