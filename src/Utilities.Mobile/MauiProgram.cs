using Microsoft.Extensions.Logging;
using Utilities.Mobile.ViewModels;
using Utilities.Core.Application.UseCases;
using Utilities.Infrastructure.Persistence;
using Utilities.Mobile.Infrastructure.Media;
using Utilities.Mobile.Infrastructure.Preferences;
using Utilities.Mobile.Infrastructure.Diagnostics;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Infrastructure.Reminders;
using Utilities.Mobile.Infrastructure.Ocr;
using Utilities.Mobile.Services;
using TesseractOcrMaui;
using System.Diagnostics;

namespace Utilities.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        StartupTiming.StartProcessTimer();
        var createAppTimer = Stopwatch.StartNew();
        var preferencesTimer = Stopwatch.StartNew();
        var preferences = new MauiUserPreferenceStore();
        AppText.Initialize(preferences.Get("language", "en"));
        preferencesTimer.Stop();
        var builder = MauiApp.CreateBuilder();
        var localLogs = new LocalFileLoggerProvider(FileSystem.AppDataDirectory);
        builder.Logging.AddProvider(localLogs);
        builder.Logging.AddFilter<LocalFileLoggerProvider>("Microsoft.EntityFrameworkCore", LogLevel.None);
        StartupTiming.Configure(localLogs.CreateLogger(StartupTiming.LogCategory), localLogs);
        StartupTiming.LogDuration("MauiProgram.LoadPreferencesAndResources", preferencesTimer);
#if !DEBUG
        builder.Logging.AddFilter<LocalFileLoggerProvider>(null, LogLevel.Warning);
#endif
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var registrationsTimer = Stopwatch.StartNew();
        builder.Services.AddUtilitiesPersistence(Path.Combine(FileSystem.AppDataDirectory, "utilities.db3"));
        builder.Services.AddSingleton<IUserPreferenceStore>(preferences);
        builder.Services.AddSingleton<IAppAppearance, MauiAppAppearance>();
        builder.Services.AddSingleton(localLogs);
        builder.Services.AddSingleton<IAppDiagnostics, MauiAppDiagnosticsExport>();
        builder.Services.AddScoped<UtilitiesApplicationService>();
        builder.Services.AddSingleton<ITemporaryMeterPhotoProvider, MauiTemporaryMeterPhotoProvider>();
        builder.Services.AddSingleton<IReadingHistoryFileTransfer, MauiReadingHistoryFileTransfer>();
        builder.Services.AddSingleton<ICameraPermissionService, AndroidCameraPermissionService>();
        builder.Services.AddSingleton<ILocalReminderScheduler, AndroidLocalReminderScheduler>();
        builder.Services.AddSingleton<INotificationPermissionService, AndroidNotificationPermissionService>();
        builder.Services.AddScoped<IReminderSynchronizer, MauiReminderSynchronizer>();
        builder.Services.AddTesseractOcr(files => files.AddFile("eng.traineddata"));
        builder.Services.AddSingleton<IReadingRecognizer, TesseractReadingRecognizer>();
        builder.Services.AddSingleton<IUserDialogs, MauiUserDialogs>();
        builder.Services.AddScoped<IAppNavigation, MauiAppNavigation>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        StartupTiming.LogDuration("MauiProgram.RegisterServices", registrationsTimer);
        var buildTimer = Stopwatch.StartNew();
        var app = builder.Build();
        StartupTiming.LogDuration("MauiProgram.BuilderBuild", buildTimer);
        StartupTiming.LogDuration("MauiProgram.CreateMauiApp total", createAppTimer);
        StartupTiming.LogMilestone("MauiProgram.CreateMauiApp completed");
        return app;
    }
}

