namespace Utilities.Mobile.Infrastructure.Reminders;

using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Infrastructure.Abstractions;

public sealed class MauiReminderSynchronizer(UtilitiesApplicationService application, ILocalReminderScheduler scheduler,
    ILogger<MauiReminderSynchronizer> logger) : IReminderSynchronizer
{
    public Task SynchronizeAsync(CancellationToken cancellationToken = default) =>
        ReminderSynchronization.RescheduleAsync(application, scheduler, logger, cancellationToken);
}
