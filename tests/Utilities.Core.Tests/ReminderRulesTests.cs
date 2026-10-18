namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Xunit;

public sealed class ReminderRulesTests
{
    [Fact]
    public void IsDue_RequiresEnabledReminderWhoseTimeHasArrived()
    {
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var due = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission, true, now);
        var later = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission, true, now.AddMinutes(1));
        var disabled = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission, false, now.AddHours(-1));

        Assert.True(ReminderRules.IsDue(due, now));
        Assert.False(ReminderRules.IsDue(later, now));
        Assert.False(ReminderRules.IsDue(disabled, now));
    }

    [Fact]
    public void GetNextOccurrence_AdvancesByConfiguredInterval()
    {
        var first = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var reminder = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission, true, first, 7, "UTC");

        var next = ReminderRules.GetNextOccurrence(reminder, first.AddDays(15));

        Assert.Equal(first.AddDays(21), next);
    }

    [Fact]
    public void GetNextOccurrence_MonthlyKeepsConfiguredDayOfMonth()
    {
        var first = new DateTimeOffset(2026, 1, 31, 9, 30, 0, TimeSpan.Zero);
        var reminder = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission, true, first, 0, "UTC");

        var next = ReminderRules.GetNextOccurrence(reminder, new DateTimeOffset(2026, 2, 28, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 31, 9, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrence_QuarterlyAndYearlyUseCalendarDates()
    {
        var quarterly = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission, true,
            new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero), 90, "UTC");
        var yearly = new ReminderSettings(ReminderOwnerType.Subscription, Guid.NewGuid(), ReminderKind.Payment, true,
            new DateTimeOffset(2024, 2, 29, 9, 0, 0, TimeSpan.Zero), 365, "UTC");

        Assert.Equal(new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
            ReminderRules.GetNextOccurrence(quarterly, new DateTimeOffset(2026, 4, 30, 10, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2025, 2, 28, 9, 0, 0, TimeSpan.Zero),
            ReminderRules.GetNextOccurrence(yearly, new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GetNextOccurrence_UsesStoredDayWhenFirstOccurrenceWasClampedToShortMonth()
    {
        var first = new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero);
        var reminder = new ReminderSettings(ReminderOwnerType.Meter, Guid.NewGuid(), ReminderKind.ReadingSubmission,
            true, first, 0, "UTC", calendarDayOfMonth: 31);

        Assert.Equal(new DateTimeOffset(2026, 3, 31, 9, 0, 0, TimeSpan.Zero),
            ReminderRules.GetNextOccurrence(reminder, first.AddMinutes(1)));
    }

    [Fact]
    public void MeterReminderSettings_SupportIndependentPaymentAndReadingKinds()
    {
        var meterId = Guid.NewGuid();
        var reading = new ReminderSettings(ReminderOwnerType.Meter, meterId, ReminderKind.ReadingSubmission, true, DateTimeOffset.UtcNow);
        var payment = new ReminderSettings(ReminderOwnerType.Meter, meterId, ReminderKind.Payment, true, DateTimeOffset.UtcNow.AddDays(1));

        Assert.NotEqual(reading.Kind, payment.Kind);
        Assert.Equal(meterId, reading.OwnerId);
        Assert.Equal(meterId, payment.OwnerId);
    }

    [Fact]
    public void ReminderSettings_RejectsReadingReminderForSubscription()
    {
        Assert.Throws<ArgumentException>(() => new ReminderSettings(
            ReminderOwnerType.Subscription, Guid.NewGuid(), ReminderKind.ReadingSubmission, true, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void ReminderSettings_RejectsInvalidCalendarDay(int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReminderSettings(ReminderOwnerType.Meter,
            Guid.NewGuid(), ReminderKind.ReadingSubmission, true, DateTimeOffset.UtcNow, 0, "UTC", day));
    }
}
