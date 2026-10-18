namespace Utilities.Mobile.ViewModels;

using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Mobile.Services;

public sealed class ServiceTypesViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    public ServiceTypesViewModel(UtilitiesApplicationService application, IUserDialogs dialogs, IAppNavigation navigation)
    {
        _application = application;
        _dialogs = dialogs;
        _navigation = navigation;
        AddCommand = new Command(async () => await _navigation.OpenNewServiceTypeAsync());
        ItemActionsCommand = new Command<ServiceTypeItem>(async item => { if (item is not null) await ItemActionsAsync(item.ServiceType); });
    }

    public Command AddCommand { get; }
    public Command<ServiceTypeItem> ItemActionsCommand { get; }
    public ObservableCollection<ServiceTypeItem> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public string TitleText => AppText.Get("ServiceTypes");
    public string AddText => AppText.Get("AddCustomType");
    public string ActiveText => AppText.Get("Active");
    public string InactiveText => AppText.Get("Inactive");
    public string BuiltInText => AppText.Get("BuiltIn");
    public string DescriptionText => AppText.Get("ServiceTypesDescription");
    public string EmptyText => AppText.Get("NoServiceTypes");
    public string EmptyIcon => "ic_services.png";

    public async Task LoadAsync(CancellationToken token = default)
    {
        Items.Clear();
        foreach (var item in await _application.GetServiceTypesAsync(token))
            Items.Add(new ServiceTypeItem(item, ServiceTypePresentation.DisplayName(item), ServiceTypePresentation.CategoryName(item.Category),
                item.DefaultUnit ?? string.Empty, item.DefaultUnit is not null, item.IsActive ? ActiveText : InactiveText, item.IsBuiltIn ? BuiltInText : string.Empty,
                ServiceTypePresentation.Icon(item), item.IsBuiltIn, ServiceTypePresentation.Glyph(item)));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task SetActiveAsync(ServiceType item, bool isActive, CancellationToken token = default)
    {
        if (isActive) await _application.ActivateServiceTypeAsync(item.Id, token);
        else await _application.DeactivateServiceTypeAsync(item.Id, token);
        await LoadAsync(token);
    }

    public async Task DeleteAsync(ServiceType item, CancellationToken token = default)
    {
        await _application.DeleteAsync(nameof(ServiceType), item.Id, token);
        await LoadAsync(token);
    }

    private async Task ItemActionsAsync(ServiceType item)
    {
        if (item.IsBuiltIn)
        {
            var unit = string.IsNullOrWhiteSpace(item.DefaultUnit) ? string.Empty : Environment.NewLine + AppText.Get("DefaultUnitSummary", item.DefaultUnit);
            await _dialogs.AlertAsync(ServiceTypePresentation.DisplayName(item),
                AppText.Get("BuiltInServiceTypeInfo", ServiceTypePresentation.CategoryName(item.Category), unit), AppText.Get("OK"));
            return;
        }
        var edit = AppText.Get("EditCustomServiceType");
        var activate = item.IsActive ? AppText.Get("Deactivate") : AppText.Get("Activate");
        var remove = AppText.Get("DeleteRestoreLater");
        var action = await _dialogs.ChooseAsync(ServiceTypePresentation.DisplayName(item), AppText.Get("Cancel"), null, edit, activate, remove);
        if (action == edit) await _navigation.OpenEditServiceTypeAsync(item.Id);
        else if (action == activate) await SetActiveAsync(item, !item.IsActive);
        else if (action == remove && await _dialogs.ConfirmAsync(AppText.Get("DeleteServiceType"), AppText.Get("ServiceTypeDeleteMessage"), AppText.Get("Delete"), AppText.Get("Cancel")))
            await DeleteAsync(item);
    }
}

public sealed record ServiceTypeItem(ServiceType ServiceType, string DisplayName, string CategoryText, string DefaultUnit, bool HasDefaultUnit,
    string StatusText, string BuiltInText, string Icon, bool IsBuiltIn, string IconGlyph)
{
    public bool UsesCustomIcon => !IsBuiltIn;
}
