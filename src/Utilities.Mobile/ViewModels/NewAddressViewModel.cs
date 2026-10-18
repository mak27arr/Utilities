namespace Utilities.Mobile.ViewModels;

using Microsoft.Maui.Controls;
using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Mobile.Services;
using System.Collections.Generic;

public sealed class NewAddressViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly List<SafeAsyncCommandBase> _commands = [];
    private string _name = string.Empty;
    private string _fullAddress = string.Empty;

    public NewAddressViewModel(UtilitiesApplicationService application, IUserDialogs dialogs, IAppNavigation navigation,
        ILogger<NewAddressViewModel> logger)
    {
        _application = application;
        _dialogs = dialogs;
        _navigation = navigation;
        SaveCommand = Track(new SafeAsyncCommand(SaveAsync, dialogs, logger, "save address", AppText.Get("NewAddress")));
        CancelCommand = Track(new SafeAsyncCommand(_ => _navigation.GoBackAsync(), dialogs, logger, "cancel address creation", AppText.Get("NewAddress")));
    }

    public SafeAsyncCommand SaveCommand { get; }
    public SafeAsyncCommand CancelCommand { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string FullAddress { get => _fullAddress; set => SetProperty(ref _fullAddress, value); }
    public string TitleText => AppText.Get("NewAddress");
    public string NameLabel => AppText.Get("AddressNameLabel");
    public string NamePlaceholder => AppText.Get("AddressNamePlaceholder");
    public string AddressLabel => AppText.Get("AddressFullAddressLabel");
    public string AddressPlaceholder => AppText.Get("AddressFullAddressPlaceholder");
    public string SaveText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");

    public void CancelPendingOperations()
    {
        foreach (var command in _commands) command.Cancel();
    }

    private T Track<T>(T command) where T : SafeAsyncCommandBase
    {
        _commands.Add(command);
        return command;
    }

    private async Task SaveAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await _dialogs.AlertAsync(AppText.Get("NewAddress"), AppText.Get("AddressNameRequired"), AppText.Get("OK"));
            return;
        }
        await _application.CreateAddressAsync(Name, FullAddress, token);
        await _navigation.GoBackAsync();
    }
}
