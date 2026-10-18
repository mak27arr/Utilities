namespace Utilities.Mobile.ViewModels;

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Mobile.Services;
using System.Collections.Generic;

public sealed class MeterReplacementViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly Guid _meterId;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly List<SafeAsyncCommandBase> _commands = [];
    private string _finalReading = string.Empty;
    private string _initialReading = "0";
    private DateTime _replacementDate = DateTime.Today;
    private TimeSpan _replacementTime = DateTime.Now.TimeOfDay;

    public MeterReplacementViewModel(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation,
        ILogger<MeterReplacementViewModel> logger)
    {
        _application = application;
        _meterId = meterId;
        _dialogs = dialogs;
        _navigation = navigation;
        SaveCommand = Track(new SafeAsyncCommand(SaveAsync, dialogs, logger, "replace meter", AppText.Get("ReplaceMeter")));
        CancelCommand = Track(new SafeAsyncCommand(_ => _navigation.GoBackAsync(), dialogs, logger, "cancel meter replacement", AppText.Get("ReplaceMeter")));
    }

    public SafeAsyncCommand SaveCommand { get; }
    public SafeAsyncCommand CancelCommand { get; }
    public string MeterName { get; private set; } = string.Empty;
    public string TitleText => AppText.Get("ReplaceMeter");
    public string HelpText => AppText.Get("MeterReplacementHelp");
    public string FinalReadingLabel => AppText.Get("FinalMeterReading");
    public string InitialReadingLabel => AppText.Get("NewMeterInitialReading");
    public string DateLabel => AppText.Get("ReplacementDate");
    public string TimeLabel => AppText.Get("ReminderTimeLabel");
    public string SaveText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");
    public string FinalReading { get => _finalReading; set => SetProperty(ref _finalReading, value); }
    public string InitialReading { get => _initialReading; set => SetProperty(ref _initialReading, value); }
    public DateTime ReplacementDate { get => _replacementDate; set => SetProperty(ref _replacementDate, value); }
    public TimeSpan ReplacementTime { get => _replacementTime; set => SetProperty(ref _replacementTime, value); }

    public void CancelPendingOperations()
    {
        foreach (var command in _commands) command.Cancel();
    }

    private T Track<T>(T command) where T : SafeAsyncCommandBase
    {
        _commands.Add(command);
        return command;
    }

    public async Task LoadAsync(CancellationToken token = default)
    {
        var meter = await _application.GetMeterAsync(_meterId, token);
        if (meter is null) throw new InvalidOperationException("The meter could not be found.");
        var serviceType = await _application.GetServiceTypeAsync(meter.ServiceTypeId, token);
        MeterName = serviceType is null ? meter.Name : ServiceTypePresentation.DisplayName(serviceType);
        OnPropertyChanged(nameof(MeterName));
    }

    private async Task SaveAsync(CancellationToken token = default)
    {
        if (!NumericInputParser.TryDouble(FinalReading, out var finalValue) || !double.IsFinite(finalValue) || finalValue < 0)
        {
            await _dialogs.AlertAsync(AppText.Get("InvalidReading"), AppText.Get("NonNegativeNumber"), AppText.Get("OK"));
            return;
        }
        if (!NumericInputParser.TryDouble(InitialReading, out var initialValue) || !double.IsFinite(initialValue) || initialValue < 0)
        {
            await _dialogs.AlertAsync(AppText.Get("InvalidReading"), AppText.Get("NonNegativeNumber"), AppText.Get("OK"));
            return;
        }

        var localDateTime = DateTime.SpecifyKind(ReplacementDate.Date + ReplacementTime, DateTimeKind.Local);
        var replacedAt = new DateTimeOffset(localDateTime);
        if (replacedAt > DateTimeOffset.Now)
        {
            await _dialogs.AlertAsync(TitleText, AppText.Get("ReplacementDateCannotBeFuture"), AppText.Get("OK"));
            return;
        }

        var assessment = await _application.PreviewReadingAsync(_meterId, finalValue, replacedAt, token: token);
        var confirmed = !assessment.RequiresReview || await _dialogs.ConfirmAsync(AppText.Get("CheckReading"),
            AppText.Get("LowerReadingWarning"), AppText.Get("SaveAnyway"), AppText.Get("Review"));
        if (!confirmed) return;

        var replacement = await _application.ReplaceMeterAsync(_meterId, finalValue, initialValue, replacedAt, confirmed, token);
        await _dialogs.AlertAsync(TitleText, AppText.Get("MeterReplacementSaved"), AppText.Get("OK"));
        await _navigation.GoBackAsync();
        await _navigation.OpenMeterAsync(replacement.Id);
    }
}
