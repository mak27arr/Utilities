namespace Utilities.Mobile;

using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class NewAddressPage : ContentPage
{
    private readonly NewAddressViewModel _viewModel;

    public NewAddressPage(UtilitiesApplicationService application, IUserDialogs dialogs, IAppNavigation navigation,
        ILogger<NewAddressViewModel> logger, IAddressListCache addressCache)
    {
        InitializeComponent();
        _viewModel = new NewAddressViewModel(application, dialogs, navigation, logger, addressCache);
        BindingContext = _viewModel;
        Title = AppText.Get("NewAddress");
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }
}
