namespace Utilities.Mobile;

using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class AddressDetailPage : ContentPage
{
    private readonly AddressDetailViewModel _viewModel;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly IUserPreferenceStore _preferences;

    public AddressDetailPage(
        UtilitiesApplicationService application,
        Guid addressId,
        IUserDialogs dialogs,
        IAppNavigation navigation,
        IUserPreferenceStore preferences,
        IAddressListCache addressCache)
    {
        InitializeComponent();
        _viewModel = new AddressDetailViewModel(application, addressId, dialogs, navigation, addressCache);
        _dialogs = dialogs;
        _navigation = navigation;
        _preferences = preferences;
        BindingContext = _viewModel;
        Title = AppText.Get("Address");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.LoadAsync();
            if (_viewModel.Address is null) await _navigation.GoBackAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("CouldNotOpenAddress"), exception);
        }
    }

    private async void OnMeterTapped(object? sender, TappedEventArgs args) => await OpenMeterFromSenderAsync(sender, openSettings: false);

    private async void OnMeterSettingsClicked(object? sender, EventArgs args) => await OpenMeterFromSenderAsync(sender, openSettings: true);

    private async Task OpenMeterFromSenderAsync(object? sender, bool openSettings)
    {
        if (sender is not BindableObject { BindingContext: MeterSummary summary }) return;
        try
        {
            if (openSettings)
                await _navigation.OpenMeterSettingsAsync(summary.Meter.Id);
            else if (summary.Meter.ReplacedAt.HasValue)
                await _navigation.OpenMeterAsync(summary.Meter.Id);
            else if (!bool.TryParse(_preferences.Get("openCameraOnMeterTap", "true"), out var openCamera) || openCamera)
                await _navigation.OpenPhotoCropAsync(summary.Meter.Id);
            else
                await _navigation.OpenManualReadingAsync(summary.Meter.Id);
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("CouldNotOpenMeter"), exception);
        }
    }
}

