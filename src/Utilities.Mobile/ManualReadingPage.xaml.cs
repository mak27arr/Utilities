namespace Utilities.Mobile;

using System.Globalization;
using Utilities.Core.Application.UseCases;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;

public partial class ManualReadingPage : ContentPage
{
    private readonly UtilitiesApplicationService _application;
    private readonly Guid _meterId;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private bool _busy;

    public ManualReadingPage(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation)
    {
        InitializeComponent();
        _application = application;
        _meterId = meterId;
        _dialogs = dialogs;
        _navigation = navigation;
        BindingContext = this;
        Title = AppText.Get("NewReading");
    }

    public string MeterTitle { get; private set; } = string.Empty;
    public string ReadingValue { get; set; } = string.Empty;
    public string CurrentReadingPlaceholder => AppText.Get("CurrentReadingValue");
    public string SaveText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var meter = await _application.GetMeterAsync(_meterId);
            if (meter is null)
            {
                await _navigation.GoBackAsync();
                return;
            }

            MeterTitle = meter.Name;
            OnPropertyChanged(nameof(MeterTitle));
            var previous = await _application.GetLatestReadingAsync(_meterId);
            PreviousReadingLabel.Text = previous is null
                ? AppText.Get("NoPreviousReading")
                : AppText.Get("PreviousReading", previous.ConfirmedValue.ToString("0.####", CultureInfo.CurrentCulture),
                    previous.MeasuredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            Dispatcher.Dispatch(() => ReadingEntry.Focus());
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("NewReading"), exception);
        }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        if (!NumericInputParser.TryDouble(ReadingEntry.Text, out var value) || !double.IsFinite(value) || value < 0)
        {
            await _dialogs.AlertAsync(AppText.Get("InvalidReading"), AppText.Get("NonNegativeNumber"), AppText.Get("OK"));
            ReadingEntry.Focus();
            return;
        }

        _busy = true;
        try
        {
            var at = DateTimeOffset.Now;
            var assessment = await _application.PreviewReadingAsync(_meterId, value, at);
            var confirmLower = !assessment.RequiresReview || await _dialogs.ConfirmAsync(AppText.Get("CheckReading"),
                AppText.Get("LowerReadingWarning"), AppText.Get("SaveAnyway"), AppText.Get("Review"));
            if (!confirmLower) return;

            await _application.AddReadingAsync(_meterId, value, at, confirmLower);
            await _navigation.GoBackAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("NewReading"), exception);
        }
        finally { _busy = false; }
    }

    private async void CancelClicked(object? sender, EventArgs e) => await _navigation.GoBackAsync();
}
