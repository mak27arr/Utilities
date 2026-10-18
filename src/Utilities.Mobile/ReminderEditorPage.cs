namespace Utilities.Mobile;

using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Infrastructure.Reminders;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class ReminderEditorPage : ContentPage
{
    private readonly ReminderEditorViewModel _viewModel;
    private readonly IUserDialogs _dialogs;

    public ReminderEditorPage(UtilitiesApplicationService application, ILocalReminderScheduler scheduler,
        INotificationPermissionService permissions, IUserDialogs dialogs, IAppNavigation navigation,
        ReminderOwnerType ownerType, Guid ownerId, string ownerName, ReminderKind kind)
    {
        InitializeComponent();
        _viewModel = new ReminderEditorViewModel(application, scheduler, permissions, dialogs, navigation, ownerType, ownerId, ownerName, kind);
        _dialogs = dialogs;
        BindingContext = _viewModel;
        Title = AppText.Get("ReminderSettings");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _viewModel.LoadAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("ReminderSettings"), exception); }
    }
}

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(UtilitiesApplicationService application, ILocalReminderScheduler scheduler, IUserDialogs dialogs,
        IAppNavigation navigation, IUserPreferenceStore preferences, IAppAppearance appearance, IAppDiagnostics diagnostics)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(application, scheduler, dialogs, navigation, preferences, appearance, diagnostics);
        BindingContext = _viewModel;
        Title = AppText.Get("Settings");
    }
}
