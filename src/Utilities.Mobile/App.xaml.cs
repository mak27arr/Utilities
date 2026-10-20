namespace Utilities.Mobile;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Utilities.Mobile.Services;
using System.Diagnostics;

public partial class App : Application
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<App> _logger;
    private readonly IUserPreferenceStore _preferences;
    private readonly IAddressListCache _addressCache;
    private readonly Dictionary<Window, IServiceScope> _windowScopes = [];

    public App(IServiceScopeFactory scopeFactory, ILogger<App> logger, IUserPreferenceStore preferences, IAddressListCache addressCache)
    {
        var constructorTimer = Stopwatch.StartNew();
        var xamlTimer = Stopwatch.StartNew();
        InitializeComponent();
        xamlTimer.Stop();
        _scopeFactory = scopeFactory;
        _logger = logger;
        _preferences = preferences;
        _addressCache = addressCache;
        UserAppTheme = preferences.Get("theme", "system") switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        StartupTiming.LogDuration("App.InitializeComponent", xamlTimer);
        StartupTiming.LogDuration("App constructor total", constructorTimer);
        StartupTiming.LogMilestone("Application instance created");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            _logger.LogCritical(exception, "Unhandled application exception. IsTerminating: {IsTerminating}", args.IsTerminating);
        else
            _logger.LogCritical("Unhandled application exception. IsTerminating: {IsTerminating}; Exception: {Exception}", args.IsTerminating, args.ExceptionObject);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) =>
        _logger.LogError(args.Exception, "Unobserved task exception.");

    protected override void OnSleep()
    {
        base.OnSleep();
        _ = SaveAddressCacheOnSleepAsync();
    }

    private async Task SaveAddressCacheOnSleepAsync()
    {
        try { await _addressCache.SaveLastSnapshotAsync(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Could not save the home address cache when the app entered the background."); }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        StartupTiming.LogMilestone("CreateWindow started");
        var scopeTimer = Stopwatch.StartNew();
        var scope = _scopeFactory.CreateScope();
        StartupTiming.LogDuration("CreateWindow.CreateScope", scopeTimer);
        try
        {
            var pageTimer = Stopwatch.StartNew();
            var page = scope.ServiceProvider.GetRequiredService<MainPage>();
            StartupTiming.LogDuration("CreateWindow.ResolveMainPage (including page constructor)", pageTimer);
            var windowTimer = Stopwatch.StartNew();
            var window = new Window(new NavigationPage(page));
            StartupTiming.LogDuration("CreateWindow.CreateNavigationPageAndWindow", windowTimer);
            _windowScopes.Add(window, scope);
            window.Destroying += (_, _) =>
            {
                _ = SaveAddressCacheOnSleepAsync();
                _windowScopes.Remove(window);
                scope.Dispose();
            };
            StartupTiming.LogMilestone("Main window created");
            return window;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public void ApplyLanguage()
    {
        if (Windows.FirstOrDefault() is not { } window || !_windowScopes.TryGetValue(window, out var scope)) return;
        if (scope.ServiceProvider.GetService<IAppNavigation>() is MauiAppNavigation navigation)
            navigation.ResetNavigationScopes();
        window.Page = new NavigationPage(scope.ServiceProvider.GetRequiredService<MainPage>());
    }

    public void ShowHome() => ApplyLanguage();
}

