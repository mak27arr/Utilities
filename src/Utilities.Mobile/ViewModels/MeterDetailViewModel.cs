namespace Utilities.Mobile.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;

public sealed class MeterDetailViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService application;
    private readonly Guid meterId;
    private readonly IUserDialogs dialogs;
    private readonly IAppNavigation navigation;
    private readonly IReadingHistoryFileTransfer historyFiles;
    private readonly ILogger<MeterDetailViewModel> logger;
    private readonly List<SafeAsyncCommandBase> asyncCommands = [];
    private ServiceType? serviceType;
    public bool ShowReadingActions { get; }

    public MeterDetailViewModel(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation,
        IReadingHistoryFileTransfer historyFiles, ILogger<MeterDetailViewModel> logger, bool settingsOnly = false)
    {
        this.application = application;
        this.meterId = meterId;
        this.dialogs = dialogs;
        this.navigation = navigation;
        this.historyFiles = historyFiles;
        this.logger = logger;
        ShowReadingActions = !settingsOnly;
        EditMeterCommand = Track(new SafeAsyncCommand(_ =>
        {
            return Meter is not null ? navigation.OpenMeterIdentifiersAsync(Meter.Id) : Task.CompletedTask;
        }, dialogs, logger, "open meter settings", AppText.Get("Meter")));
        ReadingReminderCommand = Track(new SafeAsyncCommand(_ =>
        {
            return Meter is not null ? navigation.OpenReminderEditorAsync(ReminderOwnerType.Meter, Meter.Id, Meter.Name, ReminderKind.ReadingSubmission) : Task.CompletedTask;
        }, dialogs, logger, "open reading reminder settings", AppText.Get("ReadingReminderSettings")));
        PaymentReminderCommand = Track(new SafeAsyncCommand(_ =>
        {
            return Meter is not null ? navigation.OpenReminderEditorAsync(ReminderOwnerType.Meter, Meter.Id, Meter.Name, ReminderKind.Payment) : Task.CompletedTask;
        }, dialogs, logger, "open payment reminder settings", AppText.Get("PaymentReminderSettings")));
        PhotoReadingAreaCommand = Track(new SafeAsyncCommand(_ => navigation.OpenPhotoCropAsync(meterId), dialogs, logger, "open photo capture", AppText.Get("ReadingArea")));
        AddReadingCommand = Track(new SafeAsyncCommand(_ => navigation.OpenManualReadingAsync(meterId), dialogs, logger, "open manual reading", AppText.Get("NewReading")));
        AddTariffCommand = Track(new SafeAsyncCommand(AddTariffFromPromptAsync, dialogs, logger, "add tariff", AppText.Get("NewTariff")));
        ReplaceMeterCommand = Track(new SafeAsyncCommand(_ =>
        {
            return Meter is not null ? navigation.OpenMeterReplacementAsync(Meter.Id) : Task.CompletedTask;
        }, dialogs, logger, "open meter replacement", AppText.Get("ReplaceMeter")));
        DeleteMeterCommand = Track(new SafeAsyncCommand(DeleteMeterWithConfirmationAsync, dialogs, logger, "delete meter", AppText.Get("DeleteMeter")));
        ReadingActionsCommand = Track(new SafeAsyncCommand<ReadingSummary>(async (reading, token) =>
        {
            if (reading is not null) await ReadingActionsAsync(reading.Reading, token);
        }, dialogs, logger, "edit or delete reading", AppText.Get("Reading")));
        TariffActionsCommand = Track(new SafeAsyncCommand<TariffSummary>(async (tariff, token) =>
        {
            if (tariff is not null) await DeleteTariffAsync(tariff.Tariff, token);
        }, dialogs, logger, "delete tariff", AppText.Get("TariffPeriod")));
        HistoryRowActionsCommand = Track(new SafeAsyncCommand<HistoryRow>(HandleHistoryRowAsync, dialogs, logger, "edit or delete history entry", AppText.Get("History")));
        OpenReadingHistoryCommand = Track(new SafeAsyncCommand(_ => navigation.OpenMeterHistoryAsync(meterId, tariffs: false), dialogs, logger, "open reading history", AppText.Get("ReadingHistory")));
        OpenTariffHistoryCommand = Track(new SafeAsyncCommand(_ => navigation.OpenMeterHistoryAsync(meterId, tariffs: true), dialogs, logger, "open tariff history", AppText.Get("TariffHistory")));
        ToggleChartCommand = new Command(() =>
        {
            ShowChart = !ShowChart;
            OnPropertyChanged(nameof(ChartButtonText));
        });
        ExportHistoryCommand = Track(new SafeAsyncCommand(ExportHistoryAsync, dialogs, logger, "export history", AppText.Get("ExportHistory")));
        ImportReadingsCommand = Track(new SafeAsyncCommand(ImportReadingsFromCsvAsync, dialogs, logger, "import readings", AppText.Get("ImportReadingsCsvTitle")));
    }

    public SafeAsyncCommand EditMeterCommand { get; }
    public SafeAsyncCommand ReadingReminderCommand { get; }
    public SafeAsyncCommand PaymentReminderCommand { get; }
    public SafeAsyncCommand PhotoReadingAreaCommand { get; }
    public SafeAsyncCommand AddReadingCommand { get; }
    public SafeAsyncCommand AddTariffCommand { get; }
    public SafeAsyncCommand ReplaceMeterCommand { get; }
    public SafeAsyncCommand DeleteMeterCommand { get; }
    public SafeAsyncCommand<ReadingSummary> ReadingActionsCommand { get; }
    public SafeAsyncCommand<TariffSummary> TariffActionsCommand { get; }
    public SafeAsyncCommand<HistoryRow> HistoryRowActionsCommand { get; }
    public SafeAsyncCommand OpenReadingHistoryCommand { get; }
    public SafeAsyncCommand OpenTariffHistoryCommand { get; }
    public Command ToggleChartCommand { get; }
    public SafeAsyncCommand ExportHistoryCommand { get; }
    public SafeAsyncCommand ImportReadingsCommand { get; }
    public Meter? Meter { get; private set; }
    public ObservableCollection<ReadingSummary> Readings { get; } = [];
    public ObservableCollection<TariffSummary> Tariffs { get; } = [];
    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];
    public bool IsReadingsEmpty => Readings.Count == 0;
    private bool _hasAnySavedReading;
    public bool CanImportReadings => !_hasAnySavedReading;
    public bool IsTariffsEmpty => Tariffs.Count == 0;
    public bool IsHistoryPage { get; set; }
    public bool IsTariffHistory => IsHistoryPage && !IsReadingHistory;
    public ReadingSummary? LatestReading => Readings.FirstOrDefault();
    public TariffSummary? LatestTariff => Tariffs.FirstOrDefault();
    public bool IsReadingHistory { get; set; }
    public string HistoryTitle => AppText.Get(IsReadingHistory ? "ReadingHistory" : "TariffHistory");
    public string HistoryIcon => IsReadingHistory ? "ic_meter.png" : "ic_services.png";
    private bool _showChart;
    public bool ShowChart { get => _showChart; private set => SetProperty(ref _showChart, value); }
    public string ChartButtonText => AppText.Get(ShowChart ? "HideChart" : "ShowChart");
    public string ExportHistoryText => AppText.Get("ExportCsv");
    public string HistoryExportTitle => AppText.Get("ExportHistory");
    public string ChartDescription => AppText.Get(IsReadingHistory ? "ConsumptionChartLabel" : "TariffChartLabel", Meter?.Unit ?? string.Empty);
    public IReadOnlyList<HistoryChartSample> ChartSamples
    {
        get
        {
            if (!IsReadingHistory)
                return Tariffs.OrderBy(item => item.Tariff.EffectiveFrom)
                    .Select(item => new HistoryChartSample(item.Tariff.EffectiveFrom, (double)item.Tariff.RatePerUnit)).ToArray();

            var samples = new List<HistoryChartSample>();
            double? previousValue = null;
            foreach (var summary in Readings.OrderBy(item => item.MeasuredAt))
            {
                var reading = summary.Reading;
                var consumption = reading.ConsumptionSnapshot;
                if (!consumption.HasValue && previousValue.HasValue && reading.ConfirmedValue >= previousValue.Value)
                    consumption = reading.ConfirmedValue - previousValue.Value;
                if (consumption.HasValue) samples.Add(new HistoryChartSample(reading.MeasuredAt, consumption.Value));
                previousValue = reading.ConfirmedValue;
            }
            return samples;
        }
    }
    public bool HasLatestReading => LatestReading is not null;
    public bool HasLatestTariff => LatestTariff is not null;
    public bool CanReplaceMeter => Meter is not null && !Meter.ReplacedAt.HasValue;
    public string ReplacementSummary { get; private set; } = string.Empty;
    public bool HasReplacementSummary => !string.IsNullOrEmpty(ReplacementSummary);
    public string MeterNumberSummary => AppText.Get("OptionalMeterNumberValue", Meter?.MeterNumber ?? AppText.Get("NotSet"));
    public string AccountNumberSummary => AppText.Get("OptionalAccountNumberValue", Meter?.AccountNumber ?? AppText.Get("NotSet"));
    public string EditMeterText => AppText.Get("EditMeter");
    public string ReadingReminderText => AppText.Get("ReadingReminderSettings");
    public string PaymentReminderText => AppText.Get("PaymentReminderSettings");
    public string PhotoReadingText => AppText.Get("PhotoReading");
    public string AddManualReadingText => AppText.Get("AddManualReading");
    public string AddTariffText => AppText.Get("AddTariffPeriod");
    public string ReplaceMeterText => AppText.Get("ReplaceMeter");
    public string ReadingHistoryText => AppText.Get("ReadingHistory");
    public string AllReadingsText => AppText.Get("ViewAllReadings");
    public string AllTariffsText => AppText.Get("ViewAllTariffs");
    public string NoReadingsText => AppText.Get("NoReadingsYet");
    public string TariffHistoryText => AppText.Get("TariffHistory");
    public string NoTariffsText => AppText.Get("NoTariffsYet");
    public string DeleteMeterText => AppText.Get("DeleteMeterHistory");
    public string ImportReadingsCsvText => AppText.Get("ImportReadingsCsv");
    public string ImportReadingsCsvHelpText => AppText.Get("ImportReadingsCsvHelp");
    public string MeterTitle => serviceType is null ? Meter?.Name ?? string.Empty : ServiceTypePresentation.DisplayName(serviceType);
    public string MeterDetails => Meter is null ? string.Empty : $"{Meter.Unit} · {AppText.Get(Meter.DisplayType == MeterDisplayType.Digital ? "DigitalDisplay" : "MechanicalRollers")}";

    public void CancelPendingOperations()
    {
        foreach (var command in asyncCommands) command.Cancel();
    }

    private T Track<T>(T command) where T : SafeAsyncCommandBase
    {
        asyncCommands.Add(command);
        return command;
    }

    public async Task LoadAsync(CancellationToken token = default)
    {
        Meter = await application.GetMeterAsync(meterId, token);
        serviceType = Meter is null ? null : await application.GetServiceTypeAsync(Meter.ServiceTypeId, token);
        ReplacementSummary = string.Empty;
        if (Meter?.ReplacedAt is { } replacedAt)
            ReplacementSummary = AppText.Get("MeterReplacedOn", replacedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        else if (Meter?.ReplacesMeterId is { } previousId)
        {
            var previousMeter = await application.GetMeterAsync(previousId, token);
            var replacementDate = previousMeter?.ReplacedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? AppText.Get("NotSet");
            ReplacementSummary = AppText.Get("ReplacesPreviousMeter", previousMeter?.MeterNumber ?? AppText.Get("NotSet"), replacementDate);
        }
        OnPropertyChanged(nameof(Meter));
        OnPropertyChanged(nameof(MeterTitle));
        OnPropertyChanged(nameof(CanReplaceMeter));
        OnPropertyChanged(nameof(ReplacementSummary));
        OnPropertyChanged(nameof(HasReplacementSummary));
        OnPropertyChanged(nameof(MeterNumberSummary));
        OnPropertyChanged(nameof(AccountNumberSummary));
        OnPropertyChanged(nameof(MeterDetails));
        Readings.Clear();
        Tariffs.Clear();
        HistoryRows.Clear();
        if (Meter is null) return;
        _hasAnySavedReading = await application.HasAnyReadingAsync(meterId, includeDeleted: true, token);
        var readings = IsHistoryPage
            ? await application.GetReadingsAsync(meterId, token)
            : (await application.GetLatestReadingAsync(meterId, token)) is { } latestReading ? [latestReading] : [];
        foreach (var reading in readings.OrderByDescending(row => row.MeasuredAt)) Readings.Add(new ReadingSummary(reading));
        var tariffs = IsHistoryPage
            ? await application.GetTariffsAsync(meterId, token)
            : (await application.GetLatestTariffAsync(meterId, token)) is { } latestTariff ? [latestTariff] : [];
        foreach (var tariff in tariffs.OrderByDescending(row => row.EffectiveFrom)) Tariffs.Add(new TariffSummary(tariff, Meter.Unit));
        if (IsHistoryPage)
        {
            if (IsReadingHistory)
            {
                foreach (var item in Readings)
                    HistoryRows.Add(new HistoryRow(item.ConfirmedValue.ToString("0.####", CultureInfo.CurrentCulture),
                        item.MeasuredAt.ToString("g", CultureInfo.CurrentCulture),
                        string.Join(" · ", new[] { item.HasConsumptionSnapshot ? item.ConsumptionText : null, item.HasAmountSnapshot ? item.AmountText : null }.Where(value => value is not null)),
                        "ic_meter.png", item.Reading, null));
            }
            else
            {
                foreach (var item in Tariffs)
                    HistoryRows.Add(new HistoryRow(item.DisplayText,
                        AppText.Get("TariffEffectiveFromValue", item.Tariff.EffectiveFrom.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
                        item.Tariff.EffectiveTo is { } end ? AppText.Get("TariffEffectiveToValue", end.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : AppText.Get("CurrentTariff"),
                        "ic_services.png", null, item.Tariff));
            }
        }
        OnPropertyChanged(nameof(IsReadingsEmpty));
        OnPropertyChanged(nameof(CanImportReadings));
        OnPropertyChanged(nameof(IsTariffsEmpty));
        OnPropertyChanged(nameof(LatestReading));
        OnPropertyChanged(nameof(LatestTariff));
        OnPropertyChanged(nameof(HasLatestReading));
        OnPropertyChanged(nameof(HasLatestTariff));
        OnPropertyChanged(nameof(ChartSamples));
        OnPropertyChanged(nameof(ChartDescription));
        OnPropertyChanged(nameof(EmptyHistoryText));
    }

    public string EmptyHistoryText => IsReadingHistory ? NoReadingsText : NoTariffsText;

    public async Task HandleHistoryRowAsync(HistoryRow? row, CancellationToken token = default)
    {
        if (row?.Reading is not null) await ReadingActionsAsync(row.Reading, token);
        else if (row?.Tariff is not null) await DeleteTariffAsync(row.Tariff, token);
    }

    public Task UpdateMeterIdentifiersAsync(string? meterNumber, string? accountNumber, CancellationToken token = default) =>
        application.UpdateMeterIdentifiersAsync(meterId, meterNumber, accountNumber, token);

    public Task<Utilities.Core.BusinessLogic.ReadingAssessment> PreviewReadingAsync(double value, DateTimeOffset at, Guid? excludeId = null, CancellationToken token = default) =>
        application.PreviewReadingAsync(meterId, value, at, excludeId, token);

    public Task AddReadingAsync(double value, DateTimeOffset at, bool confirmLower, CancellationToken token = default) =>
        application.AddReadingAsync(meterId, value, at, confirmLower, token);

    public Task UpdateReadingAsync(Guid readingId, double value, DateTimeOffset at, bool confirmLower, CancellationToken token = default) =>
        application.UpdateReadingAsync(readingId, value, at, confirmLower, token);

    public Task CreateTariffAsync(decimal rate, DateTimeOffset effectiveAt, CancellationToken token = default) =>
        application.CreateTariffAsync(meterId, rate, effectiveAt, token);

    public Task DeleteAsync(string entityType, Guid id, CancellationToken token = default) => application.DeleteAsync(entityType, id, token);
    public Task DeleteMeterAsync(CancellationToken token = default) => application.DeleteAsync(nameof(Meter), meterId, token);

    private async Task ReadingActionsAsync(Reading reading, CancellationToken token)
    {
        var action = await dialogs.ChooseAsync(AppText.Get("Reading"), AppText.Get("Cancel"), AppText.Get("DeleteReading"), AppText.Get("EditValue"));
        if (action == AppText.Get("DeleteReading"))
        {
            if (await dialogs.ConfirmAsync(AppText.Get("DeleteReading"), AppText.Get("ReadingMovesToDeleted"), AppText.Get("Delete"), AppText.Get("Cancel")))
                await DeleteAsync(nameof(Reading), reading.Id, token);
        }
        else if (action == AppText.Get("EditValue"))
        {
            var input = await dialogs.PromptAsync(AppText.Get("EditReading"), AppText.Get("MeasuredAt", reading.MeasuredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)), AppText.Get("Continue"), AppText.Get("Cancel"), reading.ConfirmedValue.ToString(CultureInfo.CurrentCulture), numeric: true);
            if (input is null || !NumericInputParser.TryDouble(input, out var value) || !double.IsFinite(value) || value < 0)
            {
                await dialogs.AlertAsync(AppText.Get("InvalidReading"), AppText.Get("NonNegativeNumber"), AppText.Get("OK"));
                return;
            }
            var assessment = await PreviewReadingAsync(value, reading.MeasuredAt, reading.Id, token);
            var confirmed = !assessment.RequiresReview || await dialogs.ConfirmAsync(AppText.Get("CheckReading"), AppText.Get("LowerReadingCorrection"), AppText.Get("Save"), AppText.Get("Cancel"));
            if (confirmed) await UpdateReadingAsync(reading.Id, value, reading.MeasuredAt, confirmLower: true, token);
        }
        await LoadAsync(token);
    }

    private async Task AddTariffFromPromptAsync(CancellationToken token)
    {
        var text = await dialogs.PromptAsync(AppText.Get("NewTariff"), AppText.Get("PricePerUnit"), AppText.Get("Continue"), AppText.Get("Cancel"), numeric: true);
        if (text is null || !NumericInputParser.TryDecimal(text, out var amount) || amount < 0)
        {
            await dialogs.AlertAsync(AppText.Get("InvalidTariff"), AppText.Get("NonNegativeAmount"), AppText.Get("OK"));
            return;
        }
        var dateText = await dialogs.PromptAsync(AppText.Get("EffectiveDate"), AppText.Get("TariffStartDatePrompt"), AppText.Get("Save"), AppText.Get("Cancel"), DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (dateText is null) return;
        if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            await dialogs.AlertAsync(AppText.Get("InvalidDate"), AppText.Get("EnterValidDate"), AppText.Get("OK"));
            return;
        }
        var effectiveAt = date.Date == DateTime.Today
            ? DateTimeOffset.Now
            : new DateTimeOffset(DateTime.SpecifyKind(date.Date, DateTimeKind.Local));
        try { await CreateTariffAsync(amount, effectiveAt, token); await LoadAsync(token); }
        catch (Exception exception) { await ReportErrorAsync(AppText.Get("CouldNotAddTariff"), exception); }
    }

    private async Task DeleteTariffAsync(MeterTariffPeriod tariff, CancellationToken token)
    {
        if (!await dialogs.ConfirmAsync(AppText.Get("TariffPeriod"), AppText.Get("DeleteHistoricalTariff"), AppText.Get("Delete"), AppText.Get("Cancel"))) return;
        await DeleteAsync(nameof(MeterTariffPeriod), tariff.Id, token);
        await LoadAsync(token);
    }

    private async Task ExportHistoryAsync(CancellationToken token)
    {
        try
        {
            string csv;
            if (IsReadingHistory)
            {
                csv = MeterHistoryCsvFormatter.FormatReadings(Readings.Select(item => item.Reading),
                    [AppText.Get("CsvMeasuredAt"), AppText.Get("CsvReading"), AppText.Get("CsvConsumption"), AppText.Get("CsvAmount")]);
            }
            else
            {
                csv = MeterHistoryCsvFormatter.FormatTariffs(Tariffs.Select(item => item.Tariff), Meter?.Unit ?? string.Empty,
                    [AppText.Get("CsvEffectiveFrom"), AppText.Get("CsvEffectiveTo"), AppText.Get("CsvRate"), AppText.Get("CsvUnit")]);
            }

            var safeName = string.Concat((Meter?.Name ?? "meter").Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
            var suffix = IsReadingHistory ? "readings" : "tariffs";
            await historyFiles.ShareCsvAsync($"{safeName}-{suffix}.csv", HistoryExportTitle, csv, token);
        }
        catch (Exception exception) { await ReportErrorAsync(HistoryExportTitle, exception); }
    }

    private async Task ImportReadingsFromCsvAsync(CancellationToken token)
    {
        if (Meter is null || !CanImportReadings)
        {
            await dialogs.AlertAsync(AppText.Get("ImportReadingsCsvTitle"), AppText.Get("CsvImportOnlyBeforeFirstReading"), AppText.Get("OK"));
            return;
        }

        try
        {
            var file = await historyFiles.PickCsvAsync(AppText.Get("ImportReadingsCsvTitle"), token);
            if (file is null) return;
            if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                await dialogs.AlertAsync(AppText.Get("InvalidCsv"), AppText.Get("CsvImportFailedNoChanges", AppText.Get("ImportReadingsCsvHelp")), AppText.Get("OK"));
                return;
            }

            var rows = ReadingCsvParser.Parse(file.Content);
            var imported = await application.ImportReadingsAsync(Meter.Id, rows, token);

            await LoadAsync(token);
            await dialogs.AlertAsync(AppText.Get("ImportReadingsCsvTitle"), AppText.Get("ImportCsvSuccess", imported.Count), AppText.Get("OK"));
        }
        catch (ReadingCsvRowException exception)
        {
            var key = exception.ErrorCode switch
            {
                ReadingCsvErrorCode.InvalidColumns => "CsvImportInvalidColumns",
                ReadingCsvErrorCode.InvalidDate => "CsvImportInvalidDate",
                ReadingCsvErrorCode.InvalidValue => "CsvImportInvalidValue",
                ReadingCsvErrorCode.DuplicateDate => "CsvImportDuplicateDate",
                ReadingCsvErrorCode.CalculationOverflow => "CsvImportCalculationOverflow",
                ReadingCsvErrorCode.FutureDate => "CsvImportFutureDate",
                ReadingCsvErrorCode.AfterMeterReplacement => "CsvImportAfterReplacement",
                ReadingCsvErrorCode.LowerThanPreviousReading => "CsvImportLowerThanPrevious",
                _ => "CsvImportMalformedRow"
            };
            await dialogs.AlertAsync(AppText.Get("InvalidCsv"), AppText.Get("CsvImportFailedNoChanges", AppText.Get(key, exception.LineNumber)), AppText.Get("OK"));
        }
        catch (FormatException exception)
        {
            logger.LogWarning(exception, "CSV import was rejected before any database changes were made.");
            await dialogs.AlertAsync(AppText.Get("InvalidCsv"), AppText.Get("CsvImportFailedNoChanges", AppText.Get("CsvImportMalformedFile")), AppText.Get("OK"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CSV import failed; no rows were committed.");
            await dialogs.AlertAsync(AppText.Get("ImportReadingsCsvTitle"), AppText.Get("CsvImportFailedNoChanges", AppText.Get("UnexpectedError")), AppText.Get("OK"));
        }
    }

    private async Task DeleteMeterWithConfirmationAsync(CancellationToken token)
    {
        if (!await dialogs.ConfirmAsync(AppText.Get("DeleteMeter"), AppText.Get("DeleteMeterMessage"), AppText.Get("Delete"), AppText.Get("Cancel"))) return;
        try { await DeleteMeterAsync(token); await navigation.GoBackAsync(); }
        catch (Exception exception) { await ReportErrorAsync(AppText.Get("DeleteMeter"), exception); }
    }

    private async Task ReportErrorAsync(string title, Exception exception)
    {
        logger.LogError(exception, "Meter screen operation failed: {Title}.", title);
        await dialogs.AlertAsync(title, AppText.Get("UnexpectedError"), AppText.Get("OK"));
    }

}

public sealed record HistoryRow(string Title, string Subtitle, string Detail, string IconSource, Reading? Reading, MeterTariffPeriod? Tariff)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

public sealed record TariffSummary(MeterTariffPeriod Tariff, string Unit)
{
    public string DisplayText => AppText.Get("TariffPerUnitValue", Tariff.RatePerUnit.ToString("0.####", CultureInfo.CurrentCulture), Unit,
        Tariff.EffectiveFrom.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
}

public sealed record ReadingSummary(Reading Reading)
{
    public double ConfirmedValue => Reading.ConfirmedValue;
    public DateTimeOffset MeasuredAt => ReadingTimestamp.ToLocalTime(Reading.MeasuredAt);
    public double? ConsumptionSnapshot => Reading.ConsumptionSnapshot;
    public decimal? AmountSnapshot => Reading.AmountSnapshot;
    public bool HasConsumptionSnapshot => Reading.ConsumptionSnapshot.HasValue;
    public bool HasAmountSnapshot => Reading.AmountSnapshot.HasValue;
    public string ConsumptionText => AppText.Get("ConsumptionValue", Reading.ConsumptionSnapshot?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty);
    public string AmountText => AppText.Get("EstimatedAmountValue", Reading.AmountSnapshot?.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty);
}

public sealed record HistoryChartSample(DateTimeOffset At, double Value);
