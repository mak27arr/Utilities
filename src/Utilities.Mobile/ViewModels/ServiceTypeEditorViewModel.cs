namespace Utilities.Mobile.ViewModels;

using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Mobile.Services;

public sealed class ServiceTypeEditorViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly Guid? _serviceTypeId;
    private string _name = string.Empty;
    private string _defaultUnit = string.Empty;
    private int _categoryIndex = (int)ServiceTypeCategory.Both;
    private string _selectedIconKey = ServiceIconPresets.All[0].Key;

    public ServiceTypeEditorViewModel(UtilitiesApplicationService application, IUserDialogs dialogs, IAppNavigation navigation, Guid? serviceTypeId = null)
    {
        _application = application;
        _dialogs = dialogs;
        _navigation = navigation;
        _serviceTypeId = serviceTypeId;
        SaveCommand = new Command(async () => await SaveAsync());
        CancelCommand = new Command(async () => await _navigation.GoBackAsync());
        SelectIconCommand = new Command<string>(SelectIcon);
        RefreshIconOptions();
    }

    public Command SaveCommand { get; }
    public Command CancelCommand { get; }
    public Command<string> SelectIconCommand { get; }
    public ObservableCollection<ServiceIconOption> IconOptions { get; } = [];
    public string SelectedIconKey { get => _selectedIconKey; private set => SetProperty(ref _selectedIconKey, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string DefaultUnit { get => _defaultUnit; set => SetProperty(ref _defaultUnit, value); }
    public int CategoryIndex
    {
        get => _categoryIndex;
        set
        {
            if (SetProperty(ref _categoryIndex, value)) OnPropertyChanged(nameof(IsMeterBased));
        }
    }
    public bool IsMeterBased => CategoryIndex is (int)ServiceTypeCategory.Meter or (int)ServiceTypeCategory.Both;
    public IReadOnlyList<string> CategoryOptions => [AppText.Get("MeterBased"), AppText.Get("FixedSubscription"), AppText.Get("MeterAndSubscription")];
    public string TitleText => AppText.Get(_serviceTypeId.HasValue ? "EditCustomServiceType" : "AddCustomType");
    public string NameLabel => AppText.Get("ServiceTypeNameLabel");
    public string NamePlaceholder => AppText.Get("ServiceTypeNamePlaceholder");
    public string CategoryLabel => AppText.Get("ServiceTypeCategory");
    public string UnitLabel => AppText.Get("DefaultUnit");
    public string UnitPlaceholder => AppText.Get("OptionalDefaultUnit");
    public string SaveText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");
    public string IconLabel => AppText.Get("ServiceTypeIcon");

    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (_serviceTypeId is not { } id) return;
        var serviceType = await _application.GetServiceTypeAsync(id, token);
        if (serviceType is null || serviceType.IsBuiltIn)
        {
            await _dialogs.AlertAsync(TitleText, AppText.Get("CustomServiceTypeUnavailable"), AppText.Get("OK"));
            await _navigation.GoBackAsync();
            return;
        }
        Name = serviceType.Name;
        CategoryIndex = (int)serviceType.Category;
        DefaultUnit = serviceType.DefaultUnit ?? string.Empty;
        SelectIcon(serviceType.IconKey);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await _dialogs.AlertAsync(TitleText, AppText.Get("ServiceTypeNameRequired"), AppText.Get("OK"));
            return;
        }
        try
        {
            if (_serviceTypeId is { } id)
                await _application.UpdateServiceTypeAsync(id, Name, (ServiceTypeCategory)CategoryIndex, IsMeterBased ? DefaultUnit : null, iconKey: SelectedIconKey);
            else
                await _application.CreateServiceTypeAsync(Name, (ServiceTypeCategory)CategoryIndex, IsMeterBased ? DefaultUnit : null, iconKey: SelectedIconKey);
            await _navigation.GoBackAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(TitleText, exception);
        }
    }

    private void SelectIcon(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !ServiceIconPresets.All.Any(item => item.Key == key)) return;
        SelectedIconKey = key;
        RefreshIconOptions();
    }

    private void RefreshIconOptions()
    {
        IconOptions.Clear();
        foreach (var preset in ServiceIconPresets.All)
            IconOptions.Add(new ServiceIconOption(preset.Key, preset.Glyph, preset.Key == SelectedIconKey));
    }
}

public sealed record ServiceIconOption(string Key, string Glyph, bool IsSelected);
