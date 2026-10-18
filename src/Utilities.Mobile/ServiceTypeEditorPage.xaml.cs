namespace Utilities.Mobile;

using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class ServiceTypeEditorPage : ContentPage
{
    private readonly ServiceTypeEditorViewModel _viewModel;
    private readonly IUserDialogs _dialogs;

    public ServiceTypeEditorPage(UtilitiesApplicationService application, IUserDialogs dialogs, IAppNavigation navigation, Guid? serviceTypeId = null)
    {
        InitializeComponent();
        _viewModel = new ServiceTypeEditorViewModel(application, dialogs, navigation, serviceTypeId);
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
