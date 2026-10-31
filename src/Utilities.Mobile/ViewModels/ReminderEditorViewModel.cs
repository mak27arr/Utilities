namespace Utilities.Mobile.ViewModels;

using Microsoft.Maui.Controls;
using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Infrastructure.Reminders;
using Utilities.Mobile.Services;

public sealed class ReminderEditorViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly ILocalReminderScheduler _scheduler;
    private readonly INotificationPermissionService _permissions;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly ILogger<ReminderEditorViewModel> _logger;
    private readonly ReminderOwnerType _ownerType;
    private readonly Guid _ownerId;
    private readonly string _ownerName;
    private readonly ReminderKind _kind;
    private bool _isEnabled;
    private TimeSpan _time = new(9, 0, 0);
    private int _repeatIndex;
    private int _dayOfMonth = DateTime.Today.Day;
    private ReminderSettings? _existingSchedule;

    public ReminderEditorViewModel(UtilitiesApplicationService application, ILocalReminderScheduler scheduler,
        INotificationPermissionService permissions, IUserDialogs dialogs, IAppNavigation navigation,
        ReminderOwnerType ownerType, Guid ownerId, string ownerName, ReminderKind kind,
        ILogger<ReminderEditorViewModel> logger)
    {
        _application = application;
        _scheduler = scheduler;
        _permissions = permissions;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
        _ownerType = ownerType;
        _ownerId = ownerId;
        _ownerName = ownerName;
        _kind = kind;
        SaveCommand = new Command(async () => await SaveAsync());
        CancelCommand = new Command(async () => await _navigation.GoBackAsync());
    }

    public Command SaveCommand { get; }
    public Command CancelCommand { get; }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public TimeSpan ReminderTime { get => _time; set => SetProperty(ref _time, value); }
    public int RepeatIndex { get => _repeatIndex; set { if (SetProperty(ref _repeatIndex, value)) OnPropertyChanged(nameof(IsCalendarRepeat)); } }
    public int DayOfMonth { get => _dayOfMonth; set => SetProperty(ref _dayOfMonth, value); }
    public string TitleText => AppText.Get(_kind == ReminderKind.Payment ? "PaymentReminder" : "ReadingReminder");
    public string OwnerName => _ownerName;
    public string EnableText => AppText.Get("EnableReminder");
    public string DayOfMonthLabel => AppText.Get("ReminderDayOfMonthLabel");
    public string TimeLabel => AppText.Get("ReminderTimeLabel");
    public string RepeatText => AppText.Get("RepeatRule");
    public string SaveText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");
    public string NeverText => AppText.Get("Never");
    public string DailyText => AppText.Get("Daily");
    public string WeeklyText => AppText.Get("Weekly");
    public string TwoWeeksText => AppText.Get("EveryTwoWeeks");
    public string ThirtyDaysText => AppText.Get("EveryThirtyDays");
    public string FourWeeksText => AppText.Get("EveryFourWeeks");
    public string MonthlyText => AppText.Get("Monthly");
    public string QuarterlyText => AppText.Get("Quarterly");
    public string YearlyText => AppText.Get("Yearly");
    public bool IsCalendarRepeat => RepeatIndex >= 6;
    public IReadOnlyList<string> RepeatOptions => [NeverText, DailyText, WeeklyText, TwoWeeksText, ThirtyDaysText, FourWeeksText, MonthlyText, QuarterlyText, YearlyText];
    public IReadOnlyList<int> DaysOfMonth { get; } = Enumerable.Range(1, 31).ToArray();

    public async Task LoadAsync(CancellationToken token = default)
    {
        var current = (await _application.GetRemindersAsync(_ownerId, token)).SingleOrDefault(item => item.OwnerType == _ownerType && item.Kind == _kind);
        _existingSchedule = current;
        _logger.LogInformation("Loaded reminder editor for {OwnerType} {OwnerId}, kind {ReminderKind}. Existing reminder {ReminderId}, enabled {IsEnabled}, next at {NextReminderAt}, repeat interval days {RepeatIntervalDays}.",
            _ownerType, _ownerId, _kind, current?.Id, current?.IsEnabled, current?.NextReminderAt, current?.RepeatIntervalDays);
        if (current is null) return;
        IsEnabled = current.IsEnabled;
        if (current.NextReminderAt is { } at)
        {
            ReminderTime = at.LocalDateTime.TimeOfDay;
            DayOfMonth = current.CalendarDayOfMonth ?? at.LocalDateTime.Day;
        }
        RepeatIndex = current.RepeatIntervalDays switch { 1 => 1, 7 => 2, 14 => 3, 30 => 4, 28 => 5, 0 => 6, 90 => 7, 365 => 8, _ => 0 };
    }

    private async Task SaveAsync(CancellationToken token = default)
    {
        if (IsEnabled && !await _permissions.IsGrantedAsync())
        {
            _logger.LogWarning("Notification permission is missing while saving enabled {ReminderKind} reminder for {OwnerType} {OwnerId}.", _kind, _ownerType, _ownerId);
            await _dialogs.AlertAsync(AppText.Get("NotificationPermission"), AppText.Get("NotificationPermissionInfo"), AppText.Get("Continue"));
            if (!await _permissions.RequestAsync())
            {
                _logger.LogWarning("User denied notification permission; reminder settings were not saved for {OwnerType} {OwnerId}, kind {ReminderKind}.", _ownerType, _ownerId, _kind);
                await _dialogs.AlertAsync(AppText.Get("NotificationPermission"), AppText.Get("NotificationsDenied"), AppText.Get("Continue"));
                return;
            }
            _logger.LogInformation("Notification permission was granted during reminder setup.");
        }

        var interval = RepeatIndex switch { 1 => 1, 2 => 7, 3 => 14, 4 => 30, 5 => 28, 6 => 0, 7 => 90, 8 => 365, _ => (int?)null };
        var at = ReminderScheduleRules.GetNextConfiguredAt(DayOfMonth, ReminderTime, interval, TimeZoneInfo.Local.Id,
            DateTimeOffset.Now, _existingSchedule);
        if (IsEnabled && at <= DateTimeOffset.Now)
        {
            _logger.LogWarning("Reminder setup rejected because computed date {ReminderAt} is not in the future.", at);
            await _dialogs.AlertAsync(AppText.Get("ReminderSettings"), AppText.Get("ReminderMustBeFuture"), AppText.Get("OK"));
            return;
        }
        try
        {
            var reminder = await _application.ConfigureReminderAsync(_ownerType, _ownerId, _kind, IsEnabled, at, interval, TimeZoneInfo.Local.Id, DayOfMonth, token);
            _logger.LogInformation("Saved reminder {ReminderId}: enabled {IsEnabled}, next at {ReminderAt}, repeat interval days {RepeatIntervalDays}, time zone {TimeZoneId}.", reminder.Id, reminder.IsEnabled, reminder.NextReminderAt, reminder.RepeatIntervalDays, reminder.TimeZoneId);
            await _scheduler.CancelFollowUpAsync(reminder.Id, token);
            if (reminder.IsEnabled)
            {
                var title = AppText.Get(_kind == ReminderKind.Payment ? "PaymentReminder" : "ReadingReminder");
                await _scheduler.ScheduleAsync(new LocalReminder(reminder.Id, title, AppText.Get("ReminderNamedMessage", _ownerName), at, interval), token);
            }
            else await _scheduler.CancelAsync(reminder.Id, token);
            await _navigation.GoBackAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save reminder settings for {OwnerType} {OwnerId}, kind {ReminderKind}.", _ownerType, _ownerId, _kind);
            await _dialogs.ReportErrorAsync(AppText.Get("ReminderSettings"), exception);
        }
    }
}
