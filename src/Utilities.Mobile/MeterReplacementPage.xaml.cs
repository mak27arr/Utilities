namespace Utilities.Mobile;

using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class MeterReplacementPage : ContentPage
{
    private readonly MeterReplacementViewModel _viewModel;
    private readonly IUserDialogs _dialogs;
    private readonly ILogger<MeterReplacementViewModel> _logger;
    private CancellationTokenSource? _loadCancellation;

    public MeterReplacementPage(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation,
        ILogger<MeterReplacementViewModel> logger)
    {
        InitializeComponent();
        _viewModel = new MeterReplacementViewModel(application, meterId, dialogs, navigation, logger);
        _dialogs = dialogs;
        _logger = logger;
        BindingContext = _viewModel;
        Title = AppText.Get("ReplaceMeter");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        try { await _viewModel.LoadAsync(_loadCancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load meter replacement details.");
            await _dialogs.AlertAsync(AppText.Get("ReplaceMeter"), AppText.Get("UnexpectedError"), AppText.Get("OK"));
        }
    }

    protected override void OnDisappearing()
    {
        _loadCancellation?.Cancel();
        _viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }
}
