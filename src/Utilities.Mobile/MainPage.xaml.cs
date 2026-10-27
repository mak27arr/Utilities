namespace Utilities.Mobile;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Utilities.Core.Application.UseCases;
using Utilities.Mobile.ViewModels;
using Utilities.Mobile.Services;
using Utilities.Core.Infrastructure.Abstractions;
using System.Diagnostics;

public partial class MainPage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserDialogs _dialogs;
    private readonly ILogger<MainPage> _logger;
    private bool _reminderSyncStarted;
    private bool _firstSizeAllocationLogged;
#if ANDROID
    private FirstDrawListener? _firstDrawListener;
#endif

    public MainPage(HomeViewModel viewModel, IServiceScopeFactory scopeFactory, IUserDialogs dialogs, ILogger<MainPage> logger)
    {
        var constructorTimer = Stopwatch.StartNew();
        var xamlTimer = Stopwatch.StartNew();
        InitializeComponent();
        xamlTimer.Stop();
        _viewModel = viewModel;
        _scopeFactory = scopeFactory;
        _dialogs = dialogs;
        _logger = logger;
        BindingContext = viewModel;
        HandlerChanged += (_, _) => AttachFirstDrawListener();
        Loaded += (_, _) =>
        {
            StartupTiming.LogMilestone("MainPage connected to platform visual tree");
            AttachFirstDrawListener();
        };
        var firstSizeChangeLogged = false;
        SizeChanged += (_, _) =>
        {
            if (firstSizeChangeLogged) return;
            firstSizeChangeLogged = true;
            StartupTiming.LogMilestone("MainPage received its first measured size");
        };
        StartupTiming.LogDuration("MainPage.InitializeComponent", xamlTimer);
        StartupTiming.LogDuration("MainPage constructor total", constructorTimer);
        StartupTiming.LogMilestone("MainPage constructed");
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (_firstSizeAllocationLogged || width <= 0 || height <= 0) return;
        _firstSizeAllocationLogged = true;
        StartupTiming.LogMilestone($"MainPage first nonzero layout allocation ({width:0}x{height:0})");
    }

    private void AttachFirstDrawListener()
    {
#if ANDROID
        if (_firstDrawListener is not null || Handler?.PlatformView is not Android.Views.View platformView) return;
        var observer = platformView.ViewTreeObserver;
        if (!observer.IsAlive) return;
        _firstDrawListener = new FirstDrawListener(platformView, processElapsedMilliseconds =>
        {
            _firstDrawListener = null;
            StartupTiming.LogMilestoneAt("MainPage first Android draw pass", processElapsedMilliseconds);
        });
        observer.AddOnDrawListener(_firstDrawListener);
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        StartupTiming.LogMilestone("Home page OnAppearing started");
        var loadTimer = Stopwatch.StartNew();
        try
        {
            await _viewModel.LoadAsync();
            StartupTiming.LogMilestone("Home page data load completed");
            StartReminderSynchronization();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Home page initialization failed.");
            await _dialogs.ReportErrorAsync(AppText.Get("DatabaseError"), exception);
        }
        finally
        {
            StartupTiming.LogDuration("MainPage.OnAppearing total", loadTimer, $"address count: {_viewModel.Addresses.Count}");
        }
    }

    private void StartReminderSynchronization()
    {
        if (_reminderSyncStarted) return;
        _reminderSyncStarted = true;
        _ = Task.Run(SynchronizeRemindersAsync);
    }

    private async Task SynchronizeRemindersAsync()
    {
        try
        {
            _logger.LogInformation("Starting background local reminder synchronization.");
            await _viewModel.EnsureInitializedAsync();
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IReminderSynchronizer>().SynchronizeAsync();
            _logger.LogInformation("Background local reminder synchronization completed.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Background local reminder synchronization failed.");
        }
    }

}




