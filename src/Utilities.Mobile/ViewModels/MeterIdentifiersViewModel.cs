namespace Utilities.Mobile.ViewModels;

using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;

public sealed class MeterIdentifiersViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly Guid _meterId;
    private string _meterNumber = string.Empty;
    private string _accountNumber = string.Empty;
    private string _meterName = string.Empty;
    private bool _isBusy;

    public MeterIdentifiersViewModel(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation)
    {
        _application = application;
        _meterId = meterId;
        _dialogs = dialogs;
        _navigation = navigation;
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
        CancelCommand = new Command(async () => await _navigation.GoBackAsync());
    }

    public Command SaveCommand { get; }
    public Command CancelCommand { get; }
    public string MeterNumber { get => _meterNumber; set => SetProperty(ref _meterNumber, value); }
    public string AccountNumber { get => _accountNumber; set => SetProperty(ref _accountNumber, value); }
    public string MeterName { get => _meterName; private set => SetProperty(ref _meterName, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) SaveCommand.ChangeCanExecute();
        }
    }
    public string TitleText => AppText.Get("EditMeter");
    public string DescriptionText => AppText.Get("MeterIdentifiersDescription");
    public string MeterNumberLabel => AppText.Get("MeterNumber");
    public string AccountNumberLabel => AppText.Get("AccountNumber");
    public string SaveText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");

    public async Task InitializeAsync(CancellationToken token = default)
    {
        var meter = await _application.GetMeterAsync(_meterId, token);
        if (meter is null)
        {
            await _dialogs.AlertAsync(TitleText, AppText.Get("MeterNotFound"), AppText.Get("OK"));
            await _navigation.GoBackAsync();
            return;
        }

        MeterName = meter.Name;
        MeterNumber = meter.MeterNumber ?? string.Empty;
        AccountNumber = meter.AccountNumber ?? string.Empty;
    }

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _application.UpdateMeterIdentifiersAsync(_meterId, MeterNumber, AccountNumber);
            await _navigation.GoBackAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(TitleText, exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
