namespace Utilities.Core.Infrastructure.Abstractions;

public sealed record LocalReminder(Guid Id, string Title, string Message, DateTimeOffset At, int? RepeatIntervalDays);

public interface ILocalReminderScheduler
{
    Task ScheduleAsync(LocalReminder reminder, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default);
    Task ScheduleFollowUpAsync(Guid reminderId, DateTimeOffset cycleAt, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task CancelFollowUpAsync(Guid reminderId, CancellationToken cancellationToken = default);
}
