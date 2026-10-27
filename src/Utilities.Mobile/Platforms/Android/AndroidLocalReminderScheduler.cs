namespace Utilities.Mobile.Infrastructure.Reminders;

#pragma warning disable CA1416, CA1422 // Android API calls are guarded by their minimum SDK checks below.

using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Utilities.Core.Infrastructure.Abstractions;

public sealed class AndroidLocalReminderScheduler : ILocalReminderScheduler
{
    private readonly ILogger<AndroidLocalReminderScheduler> _logger;

    public AndroidLocalReminderScheduler(ILogger<AndroidLocalReminderScheduler> logger) => _logger = logger;

    public Task ScheduleAsync(LocalReminder reminder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        EnsureChannel(context);
        var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        var notificationsEnabled = notificationManager?.AreNotificationsEnabled();
        var channel = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? notificationManager?.GetNotificationChannel(ReminderBroadcastReceiver.ChannelId)
            : null;
        _logger.LogInformation("Preparing reminder {ReminderId}: at {ReminderAt}, repeat interval days {RepeatIntervalDays}, Android notifications enabled {NotificationsEnabled}, channel importance {ChannelImportance}.",
            reminder.Id, reminder.At, reminder.RepeatIntervalDays, notificationsEnabled, channel?.Importance);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
            ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
        {
            _logger.LogWarning("Skipping local reminder {ReminderId}: Android POST_NOTIFICATIONS permission is not granted. Notifications enabled {NotificationsEnabled}, channel importance {ChannelImportance}.", reminder.Id, notificationsEnabled, channel?.Importance);
            return Task.CompletedTask;
        }

        var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        var pendingIntent = CreatePendingIntent(context, reminder);
        if (reminder.At <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Skipping local reminder {ReminderId}: scheduled time {ReminderAt} is not in the future.", reminder.Id, reminder.At);
            return Task.CompletedTask;
        }
        if (alarmManager is null)
        {
            _logger.LogError("Could not schedule local reminder {ReminderId}: Android AlarmManager is unavailable.", reminder.Id);
            return Task.CompletedTask;
        }
        try
        {
            alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, reminder.At.ToUnixTimeMilliseconds(), pendingIntent);
            _logger.LogInformation("Scheduled local reminder {ReminderId} for {ReminderAt} (UTC {ReminderUtc}, repeat interval days {RepeatIntervalDays}).", reminder.Id, reminder.At, reminder.At.UtcDateTime, reminder.RepeatIntervalDays);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Android failed to schedule local reminder {ReminderId} for {ReminderAt}.", reminder.Id, reminder.At);
            throw;
        }
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(ReminderBroadcastReceiver));
        intent.SetAction(ReminderBroadcastReceiver.FireAction);
        intent.SetData(Android.Net.Uri.Parse($"utilities://reminder/{reminderId:N}"));
        var pendingIntent = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pendingIntent is not null)
        {
            (context.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(pendingIntent);
            pendingIntent.Cancel();
            _logger.LogInformation("Cancelled local reminder {ReminderId}.", reminderId);
        }
        else _logger.LogInformation("No scheduled Android alarm found to cancel for reminder {ReminderId}.", reminderId);
        return Task.CompletedTask;
    }

    internal static PendingIntent CreatePendingIntent(Context context, LocalReminder reminder)
    {
        var intent = new Intent(context, typeof(ReminderBroadcastReceiver));
        intent.SetAction(ReminderBroadcastReceiver.FireAction);
        intent.SetData(Android.Net.Uri.Parse($"utilities://reminder/{reminder.Id:N}"));
        intent.PutExtra(ReminderBroadcastReceiver.IdExtra, reminder.Id.ToString("N"));
        intent.PutExtra(ReminderBroadcastReceiver.TitleExtra, reminder.Title);
        intent.PutExtra(ReminderBroadcastReceiver.MessageExtra, reminder.Message);
        intent.PutExtra(ReminderBroadcastReceiver.AtExtra, reminder.At.ToUnixTimeMilliseconds());
        return PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    public Task ScheduleFollowUpAsync(Guid reminderId, DateTimeOffset cycleAt, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarmManager is null)
        {
            _logger.LogError("Could not schedule 3-hour reading follow-up {ReminderId}: Android AlarmManager is unavailable.", reminderId);
            return Task.CompletedTask;
        }
        alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at.ToUnixTimeMilliseconds(), CreateFollowUpPendingIntent(context, reminderId, cycleAt));
        _logger.LogInformation("Scheduled reading follow-up {ReminderId} for {FollowUpAt}, cycle {CycleAt}.", reminderId, at, cycleAt);
        return Task.CompletedTask;
    }

    public Task CancelFollowUpAsync(Guid reminderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(ReminderBroadcastReceiver));
        intent.SetAction(ReminderBroadcastReceiver.FireFollowUpAction);
        intent.SetData(Android.Net.Uri.Parse($"utilities://reminder/followup/{reminderId:N}"));
        var pendingIntent = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pendingIntent is not null)
        {
            (context.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(pendingIntent);
            pendingIntent.Cancel();
            _logger.LogInformation("Cancelled reading follow-up alarm {ReminderId}.", reminderId);
        }
        return Task.CompletedTask;
    }

    internal static PendingIntent CreateFollowUpPendingIntent(Context context, Guid reminderId, DateTimeOffset cycleAt)
    {
        var intent = new Intent(context, typeof(ReminderBroadcastReceiver));
        intent.SetAction(ReminderBroadcastReceiver.FireFollowUpAction);
        intent.SetData(Android.Net.Uri.Parse($"utilities://reminder/followup/{reminderId:N}"));
        intent.PutExtra(ReminderBroadcastReceiver.IdExtra, reminderId.ToString("N"));
        intent.PutExtra(ReminderBroadcastReceiver.CycleAtExtra, cycleAt.ToUnixTimeMilliseconds());
        return PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    internal static PendingIntent CreateDismissFollowUpPendingIntent(Context context, Guid reminderId, DateTimeOffset cycleAt)
    {
        var intent = new Intent(context, typeof(ReminderBroadcastReceiver));
        intent.SetAction(ReminderBroadcastReceiver.DismissFollowUpAction);
        intent.SetData(Android.Net.Uri.Parse($"utilities://reminder/dismiss/{reminderId:N}/{cycleAt.ToUnixTimeMilliseconds()}"));
        intent.PutExtra(ReminderBroadcastReceiver.IdExtra, reminderId.ToString("N"));
        intent.PutExtra(ReminderBroadcastReceiver.CycleAtExtra, cycleAt.ToUnixTimeMilliseconds());
        return PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    internal static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(ReminderBroadcastReceiver.ChannelId, AppText.Get("ReminderChannel"), NotificationImportance.Default)
        {
            Description = AppText.Get("ReminderChannelDescription")
        });
    }

    internal static ILogger? GetLogger(string category)
    {
        try { return MainApplication.AppServices?.GetService<ILoggerFactory>()?.CreateLogger(category); }
        catch { return null; }
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { ReminderBroadcastReceiver.FireAction, ReminderBroadcastReceiver.FireFollowUpAction, ReminderBroadcastReceiver.DismissFollowUpAction })]
public sealed class ReminderBroadcastReceiver : BroadcastReceiver
{
    internal const string FireAction = "com.utilities.meterapp.action.FIRE_REMINDER";
    internal const string FireFollowUpAction = "com.utilities.meterapp.action.FIRE_REMINDER_FOLLOW_UP";
    internal const string DismissFollowUpAction = "com.utilities.meterapp.action.DISMISS_REMINDER_FOLLOW_UP";
    internal const string IdExtra = "reminder_id";
    internal const string TitleExtra = "reminder_title";
    internal const string MessageExtra = "reminder_message";
    internal const string AtExtra = "reminder_at";
    internal const string CycleAtExtra = "reminder_cycle_at";
    internal const string ChannelId = "local_reminders";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var logger = AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Receiver");
        if (context is null || intent is null)
        {
            logger?.LogWarning("Reminder receiver invoked with invalid context or action {Action}.", intent?.Action);
            return;
        }
        if (intent.Action == FireFollowUpAction)
        {
            var followUpPending = GoAsync();
            if (followUpPending is not null) _ = HandleFollowUpAsync(context, intent, followUpPending);
            return;
        }
        if (intent.Action == DismissFollowUpAction)
        {
            var dismissPending = GoAsync();
            if (dismissPending is not null) _ = DismissFollowUpAsync(context, intent, dismissPending);
            return;
        }
        if (intent.Action != FireAction)
        {
            logger?.LogWarning("Reminder receiver invoked with unexpected action {Action}.", intent.Action);
            return;
        }
        var reminderId = intent.GetStringExtra(IdExtra);
        logger?.LogInformation("Android delivered reminder broadcast for reminder {ReminderId} at {DeliveredAt}.", reminderId, DateTimeOffset.UtcNow);
        var pending = GoAsync();
        if (pending is not null) _ = HandleAsync(context, intent, pending);
    }

    private static async Task HandleAsync(Context context, Intent intent, PendingResult pending)
    {
        try
        {
            var services = MainApplication.AppServices;
            if (services is null)
            {
                AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Receiver")?.LogError("Reminder broadcast could not be handled: application service provider is unavailable.");
                return;
            }
            if (!Guid.TryParseExact(intent.GetStringExtra(IdExtra), "N", out var id))
            {
                AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Receiver")?.LogError("Reminder broadcast contains an invalid reminder ID: {ReminderId}.", intent.GetStringExtra(IdExtra));
                return;
            }
            using var scope = services.CreateScope();
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Utilities.Reminders.Receiver");
            var application = scope.ServiceProvider.GetRequiredService<Utilities.Core.Application.UseCases.UtilitiesApplicationService>();
            var settings = await application.GetReminderAsync(id);
            if (settings is null || !settings.IsEnabled)
            {
                logger?.LogWarning("Ignoring reminder broadcast {ReminderId}: settings missing or reminder disabled (found {SettingsFound}, enabled {IsEnabled}).", id, settings is not null, settings?.IsEnabled);
                return;
            }

            AndroidLocalReminderScheduler.EnsureChannel(context);
            var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            var channel = Build.VERSION.SdkInt >= BuildVersionCodes.O ? manager?.GetNotificationChannel(ChannelId) : null;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
            {
                logger?.LogWarning("Reminder {ReminderId} fired but notification was not posted: POST_NOTIFICATIONS permission is missing.", id);
                return;
            }
            if (manager?.AreNotificationsEnabled() == false || channel?.Importance == NotificationImportance.None)
            {
                logger?.LogWarning("Reminder {ReminderId} fired but app/channel notifications are disabled. App enabled {AppNotificationsEnabled}, channel importance {ChannelImportance}.", id, manager?.AreNotificationsEnabled(), channel?.Importance);
            }

            var title = intent.GetStringExtra(TitleExtra) ?? AppText.Get(settings.Kind == Utilities.Core.Domain.Models.ReminderKind.Payment ? "PaymentReminder" : "ReadingReminder");
            var message = intent.GetStringExtra(MessageExtra) ?? AppText.Get(settings.Kind == Utilities.Core.Domain.Models.ReminderKind.Payment ? "PaymentReminderMessage" : "ReadingReminderMessage");
            var cycleAt = intent.HasExtra(AtExtra)
                ? DateTimeOffset.FromUnixTimeMilliseconds(intent.GetLongExtra(AtExtra, 0))
                : DateTimeOffset.UtcNow;
            var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? new Notification.Builder(context, ChannelId)
                : new Notification.Builder(context);
            builder.SetContentTitle(title)
                .SetContentText(message)
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetAutoCancel(true);
            if (settings.Kind == Utilities.Core.Domain.Models.ReminderKind.ReadingSubmission)
                builder.AddAction(new Notification.Action.Builder(0, AppText.Get("ReminderNotNeeded"),
                    AndroidLocalReminderScheduler.CreateDismissFollowUpPendingIntent(context, id, cycleAt)).Build());
            var notification = builder.Build();
            if (manager is null)
            {
                logger?.LogError("Reminder {ReminderId} fired but Android NotificationManager is unavailable.", id);
            }
            else
            {
                manager.Notify(id.GetHashCode(), notification);
                logger?.LogInformation("Posted Android local notification for reminder {ReminderId}; repeat interval days {RepeatIntervalDays}.", id, settings.RepeatIntervalDays);
            }

            if (settings.RepeatIntervalDays is >= 0)
            {
                var next = Utilities.Core.BusinessLogic.ReminderRules.GetNextOccurrence(settings, DateTimeOffset.UtcNow);
                if (next.HasValue)
                {
                    var scheduler = scope.ServiceProvider.GetRequiredService<ILocalReminderScheduler>();
                    await scheduler.ScheduleAsync(new LocalReminder(id, title, message, next.Value, settings.RepeatIntervalDays));
                    logger?.LogInformation("Repeated reminder {ReminderId} scheduled for {NextReminderAt}.", id, next.Value);
                }
                else logger?.LogWarning("Reminder {ReminderId} fired but repeat rule produced no next occurrence.", id);
            }
            else logger?.LogInformation("Reminder {ReminderId} is one-time and will not repeat.", id);

            if (settings.Kind == Utilities.Core.Domain.Models.ReminderKind.ReadingSubmission)
            {
                var nextFollowUp = DateTimeOffset.UtcNow.AddHours(3);
                if (await application.BeginReadingReminderFollowUpAsync(id, cycleAt, nextFollowUp))
                {
                    var scheduler = scope.ServiceProvider.GetRequiredService<ILocalReminderScheduler>();
                    await scheduler.ScheduleFollowUpAsync(id, cycleAt, nextFollowUp);
                    logger?.LogInformation("Started every-3-hour follow-ups for reading reminder {ReminderId}, cycle {CycleAt}.", id, cycleAt);
                }
                else logger?.LogInformation("Skipped duplicate or already closed follow-up cycle for reading reminder {ReminderId}, cycle {CycleAt}.", id, cycleAt);
            }
        }
        catch (Exception exception)
        {
            AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Receiver")?.LogError(exception, "Unhandled exception while processing Android reminder broadcast.");
            Android.Util.Log.Error("UtilitiesReminder", exception.ToString());
        }
        finally { pending.Finish(); }
    }

    private static async Task HandleFollowUpAsync(Context context, Intent intent, PendingResult pending)
    {
        try
        {
            var services = MainApplication.AppServices;
            if (services is null || !Guid.TryParseExact(intent.GetStringExtra(IdExtra), "N", out var id) || !intent.HasExtra(CycleAtExtra))
            {
                AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.FollowUp")?.LogError("Follow-up alarm is missing application services, reminder ID, or cycle timestamp.");
                return;
            }
            var cycleAt = DateTimeOffset.FromUnixTimeMilliseconds(intent.GetLongExtra(CycleAtExtra, 0));
            using var scope = services.CreateScope();
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Utilities.Reminders.FollowUp");
            var application = scope.ServiceProvider.GetRequiredService<Utilities.Core.Application.UseCases.UtilitiesApplicationService>();
            var settings = await application.GetReminderAsync(id);
            if (settings is null || !settings.IsEnabled || settings.Kind != Utilities.Core.Domain.Models.ReminderKind.ReadingSubmission ||
                settings.FollowUpCycleAt != cycleAt || settings.FollowUpStatus != Utilities.Core.Domain.Models.ReminderFollowUpStatus.Active)
            {
                logger?.LogInformation("Ignoring stale follow-up alarm for reminder {ReminderId}, cycle {CycleAt}.", id, cycleAt);
                return;
            }

            var readings = await application.GetReadingsAsync(settings.OwnerId);
            if (readings.Any(reading => reading.CreatedAt >= cycleAt))
            {
                await application.StopReadingReminderFollowUpAsync(id, cycleAt, dismissed: false);
                logger?.LogInformation("Stopped follow-ups for reminder {ReminderId}: a reading was saved since {CycleAt}.", id, cycleAt);
                return;
            }

            AndroidLocalReminderScheduler.EnsureChannel(context);
            var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
                logger?.LogWarning("Reminder {ReminderId} follow-up is due, but POST_NOTIFICATIONS permission is missing; the 3-hour check will continue.", id);
            else if (manager?.AreNotificationsEnabled() == false)
                logger?.LogWarning("Reminder {ReminderId} follow-up is due, but Android notifications are disabled; the 3-hour check will continue.", id);
            else if (manager is not null)
            {
                var title = AppText.Get("ReadingReminder");
                var meter = await application.GetMeterAsync(settings.OwnerId);
                var message = meter?.Name is { Length: > 0 } name
                    ? AppText.Get("ReminderNamedMessage", name)
                    : AppText.Get("ReadingReminderMessage");
                var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
                    ? new Notification.Builder(context, ChannelId)
                    : new Notification.Builder(context);
                var notification = builder.SetContentTitle(title)
                    .SetContentText(message)
                    .SetSmallIcon(Resource.Mipmap.appicon)
                    .SetAutoCancel(true)
                    .AddAction(new Notification.Action.Builder(0, AppText.Get("ReminderNotNeeded"),
                        AndroidLocalReminderScheduler.CreateDismissFollowUpPendingIntent(context, id, cycleAt)).Build())
                    .Build();
                manager.Notify(id.GetHashCode(), notification);
                logger?.LogInformation("Posted 3-hour follow-up notification for reminder {ReminderId}, cycle {CycleAt}.", id, cycleAt);
            }
            else logger?.LogError("Reminder {ReminderId} follow-up is due, but Android NotificationManager is unavailable.", id);

            var nextAt = DateTimeOffset.UtcNow.AddHours(3);
            if (await application.AdvanceReadingReminderFollowUpAsync(id, cycleAt, nextAt))
                await scope.ServiceProvider.GetRequiredService<ILocalReminderScheduler>().ScheduleFollowUpAsync(id, cycleAt, nextAt);
        }
        catch (Exception exception)
        {
            AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.FollowUp")?.LogError(exception, "Unhandled exception while processing 3-hour reading reminder follow-up.");
            Android.Util.Log.Error("UtilitiesReminder", exception.ToString());
        }
        finally { pending.Finish(); }
    }

    private static async Task DismissFollowUpAsync(Context context, Intent intent, PendingResult pending)
    {
        try
        {
            var services = MainApplication.AppServices;
            if (services is null || !Guid.TryParseExact(intent.GetStringExtra(IdExtra), "N", out var id) || !intent.HasExtra(CycleAtExtra)) return;
            var cycleAt = DateTimeOffset.FromUnixTimeMilliseconds(intent.GetLongExtra(CycleAtExtra, 0));
            using var scope = services.CreateScope();
            var application = scope.ServiceProvider.GetRequiredService<Utilities.Core.Application.UseCases.UtilitiesApplicationService>();
            if (await application.StopReadingReminderFollowUpAsync(id, cycleAt, dismissed: true))
            {
                (context.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(id.GetHashCode());
                AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.FollowUp")?.LogInformation("User marked reading reminder {ReminderId} as not needed for cycle {CycleAt}; 3-hour follow-ups stopped.", id, cycleAt);
            }
        }
        catch (Exception exception)
        {
            AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.FollowUp")?.LogError(exception, "Failed to stop reading reminder follow-ups from notification action.");
            Android.Util.Log.Error("UtilitiesReminder", exception.ToString());
        }
        finally { pending.Finish(); }
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged })]
public sealed class ReminderRescheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Reschedule")?.LogInformation("Received Android reminder reschedule broadcast: {Action} at {ReceivedAt}.", intent?.Action, DateTimeOffset.UtcNow);
        var pending = GoAsync();
        if (pending is not null) _ = RescheduleAsync(pending);
    }

    private static async Task RescheduleAsync(PendingResult pending)
    {
        try
        {
            var services = MainApplication.AppServices;
            if (services is null)
            {
                AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Reschedule")?.LogError("Could not reschedule reminders: application service provider is unavailable.");
                return;
            }
            using var scope = services.CreateScope();
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Utilities.Reminders.Reschedule");
            var app = scope.ServiceProvider.GetRequiredService<Utilities.Core.Application.UseCases.UtilitiesApplicationService>();
            var scheduler = scope.ServiceProvider.GetRequiredService<ILocalReminderScheduler>();
            await ReminderSynchronization.RescheduleAsync(app, scheduler, logger);
        }
        catch (Exception exception)
        {
            AndroidLocalReminderScheduler.GetLogger("Utilities.Reminders.Reschedule")?.LogError(exception, "Unhandled exception while rescheduling reminders after Android system event.");
            Android.Util.Log.Error("UtilitiesReminder", exception.ToString());
        }
        finally { pending.Finish(); }
    }
}

public static class ReminderSynchronization
{
    public static async Task RescheduleAsync(Utilities.Core.Application.UseCases.UtilitiesApplicationService application, ILocalReminderScheduler scheduler, ILogger? logger = null, CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        var allReminders = await application.GetAllRemindersAsync(includeDeleted: true, token);
        foreach (var inactive in allReminders.Where(item => item.IsDeleted || !item.IsEnabled))
        {
            await scheduler.CancelAsync(inactive.Id, token);
            await scheduler.CancelFollowUpAsync(inactive.Id, token);
        }
        var reminders = allReminders.Where(item => !item.IsDeleted && item.IsEnabled).ToArray();
        logger?.LogInformation("Loaded {ReminderCount} enabled reminder settings for synchronization.", reminders.Length);
        foreach (var settings in reminders)
        {
            logger?.LogDebug("Synchronizing reminder {ReminderId} for {OwnerType} {OwnerId} ({ReminderKind}).", settings.Id, settings.OwnerType, settings.OwnerId, settings.Kind);
            var next = Utilities.Core.BusinessLogic.ReminderRules.GetNextOccurrence(settings, now);
            var hasFutureOccurrence = next.HasValue && next.Value > now;
            if (!hasFutureOccurrence)
                logger?.LogWarning("Reminder {ReminderId} has no future occurrence and will not be scheduled.", settings.Id);
            if (hasFutureOccurrence)
            {
                var localZone = TimeZoneInfo.Local;
                if (!string.Equals(settings.TimeZoneId, localZone.Id, StringComparison.Ordinal))
                {
                    TimeZoneInfo originalZone;
                    try { originalZone = string.IsNullOrWhiteSpace(settings.TimeZoneId) ? localZone : TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId); }
                    catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { originalZone = TimeZoneInfo.Utc; }
                    var wallTime = TimeZoneInfo.ConvertTime(next!.Value, originalZone).DateTime;
                    while (localZone.IsInvalidTime(wallTime)) wallTime = wallTime.AddMinutes(1);
                    var adjusted = new DateTimeOffset(wallTime, localZone.GetUtcOffset(wallTime));
                    await application.ConfigureReminderAsync(settings.OwnerType, settings.OwnerId, settings.Kind, true, adjusted, settings.RepeatIntervalDays, localZone.Id, settings.CalendarDayOfMonth, token, resetFollowUp: false);
                    next = adjusted;
                }
                var ownerName = settings.OwnerType == Utilities.Core.Domain.Models.ReminderOwnerType.Meter
                    ? (await application.GetMeterAsync(settings.OwnerId, token))?.Name
                    : (await application.GetSubscriptionAsync(settings.OwnerId, token))?.Name;
                var isPayment = settings.Kind == Utilities.Core.Domain.Models.ReminderKind.Payment;
                var title = AppText.Get(isPayment ? "PaymentReminder" : "ReadingReminder");
                var message = ownerName is null
                    ? AppText.Get(isPayment ? "PaymentReminderMessage" : "ReadingReminderMessage")
                    : AppText.Get("ReminderNamedMessage", ownerName);
                await scheduler.ScheduleAsync(new LocalReminder(settings.Id, title, message, next!.Value, settings.RepeatIntervalDays), token);
            }
            if (settings.FollowUpStatus == Utilities.Core.Domain.Models.ReminderFollowUpStatus.Active && settings.FollowUpCycleAt.HasValue)
            {
                var followUpAt = settings.FollowUpNextAt ?? now.AddHours(3);
                await scheduler.ScheduleFollowUpAsync(settings.Id, settings.FollowUpCycleAt.Value, followUpAt, token);
                logger?.LogInformation("Restored active 3-hour follow-up for reminder {ReminderId}, scheduled at {FollowUpAt}.", settings.Id, followUpAt);
            }
        }
        logger?.LogInformation("Reminder synchronization finished for {ReminderCount} settings.", reminders.Length);
    }
}

public sealed class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? [(Android.Manifest.Permission.PostNotifications, true)]
            : [];
}

