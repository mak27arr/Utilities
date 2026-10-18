namespace Utilities.Mobile;

using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class MeterIdentifiersPage : ContentPage
{
    private readonly MeterIdentifiersViewModel _viewModel;
    private readonly IUserDialogs _dialogs;

    public MeterIdentifiersPage(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation)
    {
        InitializeComponent();
        _viewModel = new MeterIdentifiersViewModel(application, meterId, dialogs, navigation);
        _dialogs = dialogs;
        BindingContext = _viewModel;
        Title = _viewModel.TitleText;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _viewModel.InitializeAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(Title, exception); }
    }
}
