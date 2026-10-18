namespace Utilities.Mobile;

using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class MeterDetailPage : ContentPage
{
    private readonly MeterDetailViewModel _viewModel;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly ILogger<MeterDetailViewModel> _logger;
    private CancellationTokenSource? _loadCancellation;
    private bool _promptManualReadingOnLoad;

    public MeterDetailPage(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation,
        IReadingHistoryFileTransfer historyFiles, ILogger<MeterDetailViewModel> logger, bool settingsOnly = false,
        bool promptManualReadingOnLoad = false)
    {
        InitializeComponent();
        _viewModel = new MeterDetailViewModel(application, meterId, dialogs, navigation, historyFiles, logger, settingsOnly);
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
        _promptManualReadingOnLoad = promptManualReadingOnLoad;
        BindingContext = _viewModel;
        Title = AppText.Get("Meter");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.LoadAsync(_loadCancellation.Token);
            if (_viewModel.Meter is null) await _navigation.GoBackAsync();
            else if (_promptManualReadingOnLoad)
            {
                _promptManualReadingOnLoad = false;
                _viewModel.AddReadingCommand.Execute(null);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load meter details.");
            await _dialogs.AlertAsync(AppText.Get("CouldNotOpenMeter"), AppText.Get("UnexpectedError"), AppText.Get("OK"));
        }
    }

    protected override void OnDisappearing()
    {
        _loadCancellation?.Cancel();
        _viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }
}
