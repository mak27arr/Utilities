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
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
            ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
        {
            _logger.LogWarning("Skipping local reminder {ReminderId}: Android notification permission is not granted.", reminder.Id);
            return Task.CompletedTask;
        }

        var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        var pendingIntent = CreatePendingIntent(context, reminder);
        if (reminder.At <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Skipping local reminder {ReminderId}: scheduled time {ReminderAt} is not in the future.", reminder.Id, reminder.At);
            return Task.CompletedTask;
        }
        alarmManager?.SetAndAllowWhileIdle(AlarmType.RtcWakeup, reminder.At.ToUnixTimeMilliseconds(), pendingIntent);
        _logger.LogInformation("Scheduled local reminder {ReminderId} for {ReminderAt}. Alarm manager available: {AlarmManagerAvailable}.", reminder.Id, reminder.At, alarmManager is not null);
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
        }
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
}

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { FireAction })]
public sealed class ReminderBroadcastReceiver : BroadcastReceiver
{
    internal const string FireAction = "com.utilities.meterapp.action.FIRE_REMINDER";
    internal const string IdExtra = "reminder_id";
    internal const string TitleExtra = "reminder_title";
    internal const string MessageExtra = "reminder_message";
    internal const string ChannelId = "local_reminders";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != FireAction) return;
        var pending = GoAsync();
        if (pending is not null) _ = HandleAsync(context, intent, pending);
    }

    private static async Task HandleAsync(Context context, Intent intent, PendingResult pending)
    {
        try
        {
            var services = MainApplication.AppServices;
            if (services is null || !Guid.TryParseExact(intent.GetStringExtra(IdExtra), "N", out var id)) return;
            using var scope = services.CreateScope();
            var application = scope.ServiceProvider.GetRequiredService<Utilities.Core.Application.UseCases.UtilitiesApplicationService>();
            var settings = await application.GetReminderAsync(id);
            if (settings is null || !settings.IsEnabled) return;

            AndroidLocalReminderScheduler.EnsureChannel(context);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
                return;

            var title = intent.GetStringExtra(TitleExtra) ?? AppText.Get(settings.Kind == Utilities.Core.Domain.Models.ReminderKind.Payment ? "PaymentReminder" : "ReadingReminder");
            var message = intent.GetStringExtra(MessageExtra) ?? AppText.Get(settings.Kind == Utilities.Core.Domain.Models.ReminderKind.Payment ? "PaymentReminderMessage" : "ReadingReminderMessage");
            var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? new Notification.Builder(context, ChannelId)
                : new Notification.Builder(context);
            var notification = builder.SetContentTitle(title)
                .SetContentText(message)
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetAutoCancel(true)
                .Build();
            (context.GetSystemService(Context.NotificationService) as NotificationManager)?.Notify(id.GetHashCode(), notification);

            if (settings.RepeatIntervalDays is >= 0)
            {
                var next = Utilities.Core.BusinessLogic.ReminderRules.GetNextOccurrence(settings, DateTimeOffset.UtcNow);
                if (next.HasValue)
                {
                    var scheduler = scope.ServiceProvider.GetRequiredService<ILocalReminderScheduler>();
                    await scheduler.ScheduleAsync(new LocalReminder(id, title, message, next.Value, settings.RepeatIntervalDays));
                }
            }
        }
        catch (Exception exception)
        {
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
        var pending = GoAsync();
        if (pending is not null) _ = RescheduleAsync(pending);
    }

    private static async Task RescheduleAsync(PendingResult pending)
    {
        try
        {
            var services = MainApplication.AppServices;
            if (services is null) return;
            using var scope = services.CreateScope();
            var app = scope.ServiceProvider.GetRequiredService<Utilities.Core.Application.UseCases.UtilitiesApplicationService>();
            var scheduler = scope.ServiceProvider.GetRequiredService<ILocalReminderScheduler>();
            await ReminderSynchronization.RescheduleAsync(app, scheduler);
        }
        catch (Exception exception) { Android.Util.Log.Error("UtilitiesReminder", exception.ToString()); }
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
            await scheduler.CancelAsync(inactive.Id, token);
        var reminders = allReminders.Where(item => !item.IsDeleted && item.IsEnabled).ToArray();
        logger?.LogInformation("Loaded {ReminderCount} enabled reminder settings for synchronization.", reminders.Length);
        foreach (var settings in reminders)
        {
            logger?.LogDebug("Synchronizing reminder {ReminderId} for {OwnerType} {OwnerId} ({ReminderKind}).", settings.Id, settings.OwnerType, settings.OwnerId, settings.Kind);
            var next = Utilities.Core.BusinessLogic.ReminderRules.GetNextOccurrence(settings, now);
            if (!next.HasValue || next.Value <= now)
            {
                logger?.LogWarning("Reminder {ReminderId} has no future occurrence and will not be scheduled.", settings.Id);
                continue;
            }
            var localZone = TimeZoneInfo.Local;
            if (!string.Equals(settings.TimeZoneId, localZone.Id, StringComparison.Ordinal))
            {
                TimeZoneInfo originalZone;
                try { originalZone = string.IsNullOrWhiteSpace(settings.TimeZoneId) ? localZone : TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId); }
                catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { originalZone = TimeZoneInfo.Utc; }
                var wallTime = TimeZoneInfo.ConvertTime(next.Value, originalZone).DateTime;
                while (localZone.IsInvalidTime(wallTime)) wallTime = wallTime.AddMinutes(1);
                var adjusted = new DateTimeOffset(wallTime, localZone.GetUtcOffset(wallTime));
                await application.ConfigureReminderAsync(settings.OwnerType, settings.OwnerId, settings.Kind, true, adjusted, settings.RepeatIntervalDays, localZone.Id, settings.CalendarDayOfMonth, token);
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
            await scheduler.ScheduleAsync(new LocalReminder(settings.Id, title, message, next.Value, settings.RepeatIntervalDays), token);
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

