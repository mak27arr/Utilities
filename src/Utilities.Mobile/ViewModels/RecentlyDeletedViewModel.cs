namespace Utilities.Mobile.ViewModels;

using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Utilities.Core.Application.Abstractions;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;

public sealed class RecentlyDeletedViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly IUserDialogs _dialogs;
    private readonly IReminderSynchronizer _reminderSynchronizer;

    public RecentlyDeletedViewModel(UtilitiesApplicationService application, IUserDialogs dialogs, IReminderSynchronizer reminderSynchronizer)
    {
        _application = application;
        _dialogs = dialogs;
        _reminderSynchronizer = reminderSynchronizer;
        RestoreCommand = new Command<RecentlyDeletedSummary>(async summary => { if (summary is not null) await RestoreCommandAsync(summary); });
    }

    public Command<RecentlyDeletedSummary> RestoreCommand { get; }
    public ObservableCollection<RecentlyDeletedSummary> Items { get; } = [];
    public string TitleText => AppText.Get("RecentlyDeleted");
    public string EmptyText => AppText.Get("NothingToRestore");
    public bool IsEmpty => Items.Count == 0;

    public async Task LoadAsync(CancellationToken token = default)
    {
        Items.Clear();
        foreach (var item in await _application.GetRecentlyDeletedAsync(token))
        {
            var entityKey = item.EntityType switch
            {
                "Address" => "Address",
                "Meter" => "Meter",
                "Subscription" => "SubscriptionEntity",
                "ServiceType" => "ServiceTypeEntity",
                "Reading" => "Reading",
                "MeterTariffPeriod" => "MeterTariffPeriod",
                "SubscriptionRate" => "SubscriptionRate",
                "ReminderSettings" => "ReminderSettings",
                _ => "RecentlyDeleted"
            };
            var displayName = item.EntityType == "ServiceType" ? ServiceTypePresentation.DisplayName(item.DisplayName) : item.DisplayName;
            Items.Add(new RecentlyDeletedSummary(item, AppText.Get("RestoreItem", AppText.Get(entityKey), displayName,
                item.DeletedAt.ToLocalTime().ToString("g", AppText.Culture)), "ic_restore.png"));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task RestoreAsync(RecentlyDeletedItem item, CancellationToken token = default)
    {
        await _application.RestoreAsync(item, token);
        await _reminderSynchronizer.SynchronizeAsync(token);
        var summary = Items.FirstOrDefault(row => row.Item.Id == item.Id);
        if (summary is not null) Items.Remove(summary);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task RestoreCommandAsync(RecentlyDeletedSummary summary)
    {
        try { await RestoreAsync(summary.Item); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("RecentlyDeleted"), exception); }
    }
}

public sealed record RecentlyDeletedSummary(RecentlyDeletedItem Item, string DisplayText, string Icon);
