namespace Utilities.Mobile;

using Utilities.Core.Application.UseCases;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class ServiceTypesPage : ContentPage
{
    private readonly ServiceTypesViewModel _viewModel;
    private readonly IUserDialogs _dialogs;

    public ServiceTypesPage(UtilitiesApplicationService application, IUserDialogs dialogs, IAppNavigation navigation)
    {
        InitializeComponent();
        _viewModel = new ServiceTypesViewModel(application, dialogs, navigation);
        _dialogs = dialogs;
        BindingContext = _viewModel;
        Title = AppText.Get("ServiceTypes");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _viewModel.LoadAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("ServiceTypes"), exception); }
    }
}

public partial class RecentlyDeletedPage : ContentPage
{
    private readonly RecentlyDeletedViewModel _viewModel;
    private readonly IUserDialogs _dialogs;

    public RecentlyDeletedPage(UtilitiesApplicationService application, IUserDialogs dialogs, IReminderSynchronizer reminderSynchronizer, IAddressListCache addressCache)
    {
        InitializeComponent();
        _viewModel = new RecentlyDeletedViewModel(application, dialogs, reminderSynchronizer, addressCache);
        _dialogs = dialogs;
        BindingContext = _viewModel;
        Title = AppText.Get("RecentlyDeleted");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _viewModel.LoadAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("RecentlyDeleted"), exception); }
    }
}
