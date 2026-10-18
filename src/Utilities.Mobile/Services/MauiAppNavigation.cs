namespace Utilities.Mobile.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Infrastructure.Reminders;
using Utilities.Mobile.ViewModels;

public sealed class MauiAppNavigation(
    IServiceScopeFactory scopeFactory,
    ITemporaryMeterPhotoProvider photos,
    ILocalReminderScheduler reminders,
    IUserDialogs dialogs,
    ILoggerFactory loggerFactory,
    INotificationPermissionService permissions,
    ICameraPermissionService cameraPermission,
    IReadingHistoryFileTransfer historyFiles,
    IUserPreferenceStore preferences,
    IAppAppearance appearance,
    IAppDiagnostics diagnostics) : IAppNavigation, IDisposable
{
    private readonly Dictionary<Page, IServiceScope> _pageScopes = [];
    private NavigationPage? _navigationPage;

    public Task OpenAddressAsync(Guid addressId) => PushScopedAsync(services => new AddressDetailPage(Application(services), addressId, dialogs, this, preferences));
    public Task OpenNewAddressAsync() => PushScopedAsync(services => new NewAddressPage(Application(services), dialogs, this, loggerFactory.CreateLogger<NewAddressViewModel>()));
    public Task OpenMeterAsync(Guid meterId) => PushScopedAsync(services => new MeterDetailPage(Application(services), meterId, dialogs, this, historyFiles, loggerFactory.CreateLogger<MeterDetailViewModel>()));
    public Task OpenMeterForManualReadingAsync(Guid meterId) => PushScopedAsync(services => new MeterDetailPage(Application(services), meterId, dialogs, this, historyFiles, loggerFactory.CreateLogger<MeterDetailViewModel>(), promptManualReadingOnLoad: true));
    public Task OpenMeterSettingsAsync(Guid meterId) => PushScopedAsync(services => new MeterDetailPage(Application(services), meterId, dialogs, this, historyFiles, loggerFactory.CreateLogger<MeterDetailViewModel>(), settingsOnly: true));
    public Task OpenMeterIdentifiersAsync(Guid meterId) => PushScopedAsync(services => new MeterIdentifiersPage(Application(services), meterId, dialogs, this));
    public Task OpenMeterReplacementAsync(Guid meterId) => PushScopedAsync(services => new MeterReplacementPage(Application(services), meterId, dialogs, this, loggerFactory.CreateLogger<MeterReplacementViewModel>()));
    public Task OpenMeterHistoryAsync(Guid meterId, bool tariffs) => PushScopedAsync(services => new MeterHistoryPage(Application(services), meterId, dialogs, this, historyFiles, tariffs, loggerFactory.CreateLogger<MeterDetailViewModel>()));
    public Task OpenServiceTypesAsync() => PushScopedAsync(services => new ServiceTypesPage(Application(services), dialogs, this));
    public Task OpenNewServiceTypeAsync() => PushScopedAsync(services => new ServiceTypeEditorPage(Application(services), dialogs, this));
    public Task OpenEditServiceTypeAsync(Guid serviceTypeId) => PushScopedAsync(services => new ServiceTypeEditorPage(Application(services), dialogs, this, serviceTypeId));
    public Task OpenRecentlyDeletedAsync() => PushScopedAsync(services => new RecentlyDeletedPage(Application(services), dialogs,
        services.GetRequiredService<IReminderSynchronizer>()));
    public Task OpenSettingsAsync() => PushScopedAsync(services => new SettingsPage(Application(services), reminders, dialogs, this, preferences, appearance, diagnostics));
    public Task OpenReminderEditorAsync(ReminderOwnerType ownerType, Guid ownerId, string ownerName, ReminderKind kind) =>
        PushScopedAsync(services => new ReminderEditorPage(Application(services), reminders, permissions, dialogs, this, ownerType, ownerId, ownerName, kind));
    public Task OpenPhotoCropAsync(Guid meterId) => PushScopedAsync(services => new PhotoCropPage(Application(services), meterId, photos, cameraPermission, dialogs, this));
    public Task GoBackAsync() => CurrentNavigation.PopAsync();

    public void ShowHome()
    {
        if (Microsoft.Maui.Controls.Application.Current is App app) app.ApplyLanguage();
    }

    public void ResetNavigationScopes()
    {
        foreach (var scope in _pageScopes.Values) scope.Dispose();
        _pageScopes.Clear();
        if (_navigationPage is not null) _navigationPage.Popped -= OnPagePopped;
        _navigationPage = null;
    }

    public void Dispose() => ResetNavigationScopes();

    private UtilitiesApplicationService Application(IServiceProvider services) =>
        services.GetRequiredService<UtilitiesApplicationService>();

    private async Task PushScopedAsync(Func<IServiceProvider, Page> createPage)
    {
        var navigation = CurrentNavigation;
        var scope = scopeFactory.CreateScope();
        Page page;
        try
        {
            page = createPage(scope.ServiceProvider);
            await navigation.PushAsync(page);
            _pageScopes.Add(page, scope);
            if (_navigationPage is null && CurrentNavigationPage is { } root)
            {
                _navigationPage = root;
                root.Popped += OnPagePopped;
            }
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private void OnPagePopped(object? sender, NavigationEventArgs args)
    {
        if (_pageScopes.Remove(args.Page, out var scope)) scope.Dispose();
        if (_pageScopes.Count == 0 && _navigationPage is not null)
        {
            _navigationPage.Popped -= OnPagePopped;
            _navigationPage = null;
        }
    }

    private static INavigation CurrentNavigation => Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
        ?? throw new InvalidOperationException("No active MAUI navigation stack is available.");

    private static NavigationPage? CurrentNavigationPage => Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page as NavigationPage;
}
