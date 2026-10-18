namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Xunit;

public sealed class ReminderScheduleRulesTests
{
    [Fact]
    public void GetNextConfiguredAt_DailyReminderSavedAfterItsTimeMovesToTomorrow()
    {
        var now = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

        var next = ReminderScheduleRules.GetNextConfiguredAt(15, new TimeSpan(9, 0, 0), 1, "UTC", now);

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextConfiguredAt_ResavingMonthlyReminderKeepsDayThirtyOneAnchor()
    {
        var now = new DateTimeOffset(2026, 2, 28, 10, 45, 0, TimeSpan.Zero);
        var existing = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission,
            true, new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero), 0, "UTC", 31);

        var next = ReminderScheduleRules.GetNextConfiguredAt(31, new TimeSpan(10, 30, 0), 0, "UTC", now, existing);

        Assert.Equal(new DateTimeOffset(2026, 3, 31, 10, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextConfiguredAt_QuarterlyAndYearlyUseTheirSelectedCalendarCadence()
    {
        var quarterly = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.Payment,
            true, new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero), 90, "UTC", 31);
        var yearly = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.Payment,
            true, new DateTimeOffset(2024, 2, 29, 9, 0, 0, TimeSpan.Zero), 365, "UTC", 29);

        Assert.Equal(new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
            ReminderScheduleRules.GetNextConfiguredAt(31, new TimeSpan(9, 0, 0), 90, "UTC",
                new DateTimeOffset(2026, 4, 30, 10, 0, 0, TimeSpan.Zero), quarterly));
        Assert.Equal(new DateTimeOffset(2025, 2, 28, 9, 0, 0, TimeSpan.Zero),
            ReminderScheduleRules.GetNextConfiguredAt(29, new TimeSpan(9, 0, 0), 365, "UTC",
                new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero), yearly));
    }
}
