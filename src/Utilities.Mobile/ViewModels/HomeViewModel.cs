namespace Utilities.Mobile.ViewModels;

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Mobile.Services;
using System.Diagnostics;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly IAppNavigation _navigation;
    private readonly IUserDialogs _dialogs;
    private readonly ILogger<HomeViewModel> _logger;
    private readonly IAddressListCache _addressCache;
    private bool _initialized;

    public HomeViewModel(UtilitiesApplicationService application, IAppNavigation navigation, IUserDialogs dialogs, ILogger<HomeViewModel> logger, IAddressListCache addressCache)
    {
        _application = application;
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger;
        _addressCache = addressCache;
        AddAddressCommand = new Command(async () => await _navigation.OpenNewAddressAsync());
        OpenSettingsCommand = new Command(async () => await _navigation.OpenSettingsAsync());
        OpenAddressCommand = new Command<AddressListItem>(async address =>
        {
            if (address is null) return;
            try
            {
                _logger.LogInformation("Opening address {AddressId} from the home list.", address.Id);
                await _navigation.OpenAddressAsync(address.Id);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to open address {AddressId} from the home list.", address.Id);
                await _dialogs.ReportErrorAsync(AppText.Get("CouldNotOpenAddress"), exception);
            }
        });
    }

    public Command AddAddressCommand { get; }
    public Command OpenSettingsCommand { get; }
    public Command<AddressListItem> OpenAddressCommand { get; }
    public ObservableCollection<AddressListItem> Addresses { get; } = [];
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    private string _emptyState = AppText.Get("NoAddresses");
    public string EmptyState { get => _emptyState; private set => SetProperty(ref _emptyState, value); }
    public string LocalOnlyText => AppText.Get("LocalOnly");
    public string AddAddressText => AppText.Get("AddAddress");
    public string SettingsText => AppText.Get("Settings");
    public string YourAddressesText => AppText.Get("YourAddresses");
    public string HomeHeadline => AppText.Get("HomeHeadline");
    public string AddAddressHint => AppText.Get("AddAddressHint");
    public bool IsEmpty => Addresses.Count == 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        var loadTimer = Stopwatch.StartNew();
        IsBusy = true;
        try
        {
            var cacheReadTimer = Stopwatch.StartNew();
            var cachedAddresses = await _addressCache.ReadAsync(cancellationToken);
            StartupTiming.LogDuration("HomeViewModel.AddressCache.ReadAsync", cacheReadTimer,
                cachedAddresses is null ? "cache miss" : $"cache hit; address count: {cachedAddresses.Count}");
            if (cachedAddresses is not null)
            {
                var populateTimer = Stopwatch.StartNew();
                Addresses.Clear();
                foreach (var address in cachedAddresses)
                    Addresses.Add(new AddressListItem(address.Id, address.Name, OpenAddressCommand));
                StartupTiming.LogDuration("HomeViewModel.PopulateCachedAddresses", populateTimer, $"added {Addresses.Count} addresses");
            }
            else
            {
                if (!_initialized)
                {
                    var initializeTimer = Stopwatch.StartNew();
                    await _application.InitializeAsync(cancellationToken);
                    StartupTiming.LogDuration("HomeViewModel.Application.InitializeAsync", initializeTimer);
                    _initialized = true;
                }
                var addresses = await _application.GetAddressesAsync(cancellationToken);
                Addresses.Clear();
                foreach (var address in addresses)
                    Addresses.Add(new AddressListItem(address.Id, address.Name, OpenAddressCommand));
                _addressCache.RememberSnapshot(addresses.Select(address => new CachedAddress(address.Id, address.Name)));
            }
            StartupTiming.LogDuration("HomeViewModel.LoadAddressList", loadTimer, $"source: {(cachedAddresses is null ? "database" : "cache")}; count: {Addresses.Count}");
            var notifyTimer = Stopwatch.StartNew();
            OnPropertyChanged(nameof(HasAddresses));
            OnPropertyChanged(nameof(IsEmpty));
            StartupTiming.LogDuration("HomeViewModel.NotifyBindings", notifyTimer);
        }
        finally
        {
            IsBusy = false;
            StartupTiming.LogDuration("HomeViewModel.LoadAsync total", loadTimer, $"address count: {Addresses.Count}");
        }
    }

    public bool HasAddresses => Addresses.Count > 0;

}

public sealed record AddressListItem(Guid Id, string Name, Command<AddressListItem> OpenCommand);

