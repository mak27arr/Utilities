using Microsoft.Extensions.Logging;
using Utilities.Mobile.ViewModels;
using Utilities.Core.Application.UseCases;
using Utilities.Infrastructure.Persistence;
using Utilities.Mobile.Infrastructure.Media;
using Utilities.Mobile.Infrastructure.Preferences;
using Utilities.Mobile.Infrastructure.Diagnostics;
using Utilities.Mobile.Infrastructure.Persistence;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Infrastructure.Reminders;
using Utilities.Mobile.Infrastructure.Ocr;
using Utilities.Mobile.Services;
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
        builder.Logging.AddFilter<LocalFileLoggerProvider>("Utilities.Reminders", LogLevel.Information);
        builder.Logging.AddFilter<LocalFileLoggerProvider>("Utilities.Mobile.Infrastructure.Reminders", LogLevel.Information);
        builder.Logging.AddFilter<LocalFileLoggerProvider>("Utilities.Mobile.ViewModels.ReminderEditorViewModel", LogLevel.Information);
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
        builder.Services.AddSingleton<IAddressListCache, JsonAddressListCache>();
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
        builder.Services.AddSingleton<MlKitReadingRecognizer>();
        builder.Services.AddSingleton<IReadingRecognizer>(services =>
            services.GetRequiredService<MlKitReadingRecognizer>());
        builder.Services.AddSingleton<MlKitReadingAreaSuggester>();
        builder.Services.AddSingleton<YoloReadingAreaSuggester>();
        builder.Services.AddSingleton<IReadingAreaSuggester>(services =>
            services.GetRequiredService<YoloReadingAreaSuggester>());
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
        StartDatabaseConnectionWarmup(app.Services);
        StartupTiming.LogDuration("MauiProgram.BuilderBuild", buildTimer);
        StartupTiming.LogDuration("MauiProgram.CreateMauiApp total", createAppTimer);
        StartupTiming.LogMilestone("MauiProgram.CreateMauiApp completed");
        return app;
    }

    private static void StartDatabaseConnectionWarmup(IServiceProvider services)
    {
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Utilities.DatabaseWarmup");
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IDatabaseConnectionWarmup>().WarmupAsync();
                logger.LogInformation("Background database connection warmup completed.");
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Background database connection warmup failed.");
            }
        });
    }
}

