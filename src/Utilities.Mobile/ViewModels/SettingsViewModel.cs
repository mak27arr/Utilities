namespace Utilities.Mobile.ViewModels;

using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Infrastructure.Reminders;
using Utilities.Mobile.Services;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly ILocalReminderScheduler _scheduler;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly IUserPreferenceStore _preferences;
    private readonly IAppAppearance _appearance;
    private readonly IAppDiagnostics _diagnostics;
    private int _languageIndex;
    private int _themeIndex;

    public SettingsViewModel(UtilitiesApplicationService application, ILocalReminderScheduler scheduler, IUserDialogs dialogs,
        IAppNavigation navigation, IUserPreferenceStore preferences, IAppAppearance appearance, IAppDiagnostics diagnostics)
    {
        _application = application;
        _scheduler = scheduler;
        _dialogs = dialogs;
        _navigation = navigation;
        _preferences = preferences;
        _appearance = appearance;
        _diagnostics = diagnostics;
        _languageIndex = AppText.Culture.Name.StartsWith("uk", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _themeIndex = preferences.Get("theme", "system") switch { "light" => 1, "dark" => 2, _ => 0 };
        SaveLanguageCommand = new Command(SaveLanguage);
        SaveThemeCommand = new Command(SaveTheme);
        ClearAllCommand = new Command(async () => await ConfirmClearAllAsync());
        OpenServiceTypesCommand = new Command(async () => await _navigation.OpenServiceTypesAsync());
        OpenRecentlyDeletedCommand = new Command(async () => await _navigation.OpenRecentlyDeletedAsync());
        ExportDiagnosticsCommand = new Command(async () => await ExportDiagnosticsAsync());
    }

    public Command SaveLanguageCommand { get; }
    public Command SaveThemeCommand { get; }
    public Command ClearAllCommand { get; }
    public Command OpenServiceTypesCommand { get; }
    public Command OpenRecentlyDeletedCommand { get; }
    public Command ExportDiagnosticsCommand { get; }
    public int LanguageIndex { get => _languageIndex; set => SetProperty(ref _languageIndex, value); }
    public int ThemeIndex { get => _themeIndex; set => SetProperty(ref _themeIndex, value); }
    public string TitleText => AppText.Get("Settings");
    public string LanguageText => AppText.Get("Language");
    public string ThemeText => AppText.Get("Theme");
    public string EnglishText => AppText.Get("English");
    public string UkrainianText => AppText.Get("Ukrainian");
    public string SaveText => AppText.Get("Save");
    public string ClearAllText => AppText.Get("ClearAllData");
    public string ServiceTypesText => AppText.Get("ServiceTypes");
    public string RecentlyDeletedText => AppText.Get("RecentlyDeleted");
    public string PreferencesSectionText => AppText.Get("PreferencesSection");
    public string DataManagementSectionText => AppText.Get("DataManagementSection");
    public string PrivacySectionText => AppText.Get("PrivacySection");
    public string LocalDataDescription => AppText.Get("LocalDataDescription");
    public string ExportDiagnosticsText => AppText.Get("ExportDiagnostics");
    public string ExportDiagnosticsDescription => AppText.Get("ExportDiagnosticsDescription");
    public string ServiceTypesDescription => AppText.Get("ServiceTypesSettingsSubtitle");
    public string RecentlyDeletedDescription => AppText.Get("RecentlyDeletedSettingsSubtitle");
    public string ClearAllDescription => AppText.Get("ClearAllDataDescription");
    public string ServiceTypesIcon => "ic_services.png";
    public string RecentlyDeletedIcon => "ic_restore.png";
    public string LanguageIcon => "ic_language.png";
    public string StorageIcon => "ic_storage.png";
    public IReadOnlyList<string> Languages => [EnglishText, UkrainianText];
    public IReadOnlyList<string> Themes => [AppText.Get("SystemTheme"), AppText.Get("LightTheme"), AppText.Get("DarkTheme")];

    public async Task ClearAllDataAsync(CancellationToken token = default)
    {
        foreach (var reminder in await _application.GetEnabledRemindersAsync(token))
            await _scheduler.CancelAsync(reminder.Id, token);
        await _application.ClearAllDataAsync(token);
    }

    private void SaveLanguage()
    {
        var language = LanguageIndex == 1 ? "uk" : "en";
        _preferences.Set("language", language);
        AppText.SetLanguage(language);
        _navigation.ShowHome();
    }

    private void SaveTheme()
    {
        var value = ThemeIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        _preferences.Set("theme", value);
        _appearance.ApplyTheme(value);
    }

    private async Task ConfirmClearAllAsync()
    {
        if (!await _dialogs.ConfirmAsync(AppText.Get("ClearAllData"), AppText.Get("ClearAllDataMessage"), AppText.Get("Clear"), AppText.Get("Cancel"))) return;
        try
        {
            await ClearAllDataAsync();
            _navigation.ShowHome();
        }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("ClearAllData"), exception); }
    }

    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            await _diagnostics.ExportAsync();
            await _dialogs.AlertAsync(AppText.Get("ExportDiagnostics"), AppText.Get("DiagnosticsReadyToShare"), AppText.Get("OK"));
        }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("ExportDiagnostics"), exception); }
    }
}
