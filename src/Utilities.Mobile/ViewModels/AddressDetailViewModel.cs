namespace Utilities.Mobile.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Utilities.Mobile.Services;

public sealed class AddressDetailViewModel : ViewModelBase
{
    private readonly UtilitiesApplicationService _application;
    private readonly Guid _addressId;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;

    public AddressDetailViewModel(UtilitiesApplicationService application, Guid addressId, IUserDialogs dialogs, IAppNavigation navigation)
    {
        _application = application;
        _addressId = addressId;
        _dialogs = dialogs;
        _navigation = navigation;
        EditAddressCommand = new Command(async () => await EditAddressAsync());
        ArchiveCommand = new Command(async () => await ArchiveAsync());
        AddMeterCommand = new Command(async () => await AddMeterAsync());
        AddSubscriptionCommand = new Command(async () => await AddSubscriptionAsync());
        SubscriptionActionsCommand = new Command<SubscriptionSummary>(async item => { if (item is not null) await SubscriptionActionsAsync(item); });
    }

    public Command EditAddressCommand { get; }
    public Command ArchiveCommand { get; }
    public Command AddMeterCommand { get; }
    public Command AddSubscriptionCommand { get; }
    public Command<SubscriptionSummary> SubscriptionActionsCommand { get; }
    public Address? Address { get; private set; }
    public ObservableCollection<MeterSummary> Meters { get; } = [];
    public ObservableCollection<SubscriptionSummary> Subscriptions { get; } = [];
    public bool HasMeters => Meters.Count > 0;
    public bool HasSubscriptions => Subscriptions.Count > 0;
    public bool IsMetersEmpty => Meters.Count == 0;
    public bool IsSubscriptionsEmpty => Subscriptions.Count == 0;
    public bool HasFullAddress => !string.IsNullOrWhiteSpace(Address?.FullAddress);
    public string NoMetersText => AppText.Get("NoMeters");
    public string NoSubscriptionsText => AppText.Get("NoSubscriptions");
    public string MetersText => AppText.Get("Meters");
    public string SubscriptionsText => AppText.Get("Subscriptions");
    public string EditAddressText => AppText.Get("EditAddress");
    public string ArchiveAddressText => AppText.Get("ArchiveAddress");
    public string AddMeterText => AppText.Get("AddMeter");
    public string AddSubscriptionText => AppText.Get("AddSubscription");
    public string AddressSettingsText => AppText.Get("AddressSettings");

    public async Task LoadAsync(CancellationToken token = default)
    {
        Address = await _application.GetAddressAsync(_addressId, token);
        OnPropertyChanged(nameof(Address));
        Meters.Clear();
        Subscriptions.Clear();
        if (Address is null) return;
        foreach (var meter in await _application.GetMetersAsync(_addressId, token))
        {
            var latest = await _application.GetLatestReadingAsync(meter.Id, token);
            var serviceType = await _application.GetServiceTypeAsync(meter.ServiceTypeId, token);
            Meters.Add(new MeterSummary(meter, latest, serviceType is null ? meter.Name : ServiceTypePresentation.DisplayName(serviceType),
                serviceType is null ? "ic_meter.png" : ServiceTypePresentation.Icon(serviceType),
                serviceType is not null && !serviceType.IsBuiltIn, serviceType is null ? "🔢" : ServiceTypePresentation.Glyph(serviceType)));
        }
        foreach (var subscription in await _application.GetSubscriptionsAsync(_addressId, token))
        {
            var rates = await _application.GetSubscriptionRatesAsync(subscription.Id, token);
            var fee = TariffRules.SelectSubscriptionRate(DateTimeOffset.UtcNow, rates)?.MonthlyFee;
            var type = await _application.GetServiceTypeAsync(subscription.ServiceTypeId, token);
            Subscriptions.Add(new SubscriptionSummary(subscription, fee, type is null ? subscription.Name : ServiceTypePresentation.DisplayName(type),
                type is null ? "ic_subscription.png" : ServiceTypePresentation.Icon(type), type is not null && !type.IsBuiltIn,
                type is null ? "📄" : ServiceTypePresentation.Glyph(type)));
        }
        OnPropertyChanged(nameof(HasMeters));
        OnPropertyChanged(nameof(HasSubscriptions));
        OnPropertyChanged(nameof(IsMetersEmpty));
        OnPropertyChanged(nameof(IsSubscriptionsEmpty));
        OnPropertyChanged(nameof(HasFullAddress));
    }

    public Task UpdateAddressAsync(string name, string? fullAddress, CancellationToken token = default) =>
        _application.UpdateAddressAsync(_addressId, name, fullAddress, token);

    public Task ArchiveAsync(CancellationToken token = default) => _application.DeleteAsync(nameof(Address), _addressId, token);

    public Task CreateMeterAsync(Guid serviceTypeId, string name, string unit, MeterDisplayType displayType, double? initial, CancellationToken token = default) =>
        _application.CreateMeterAsync(_addressId, serviceTypeId, name, unit, displayType, initial, token);

    public Task CreateSubscriptionAsync(Guid serviceTypeId, string name, decimal fee, CancellationToken token = default) =>
        _application.CreateSubscriptionAsync(_addressId, serviceTypeId, name, fee, DateTimeOffset.UtcNow, token);

    public Task DeleteSubscriptionAsync(Subscription subscription, CancellationToken token = default) =>
        _application.DeleteAsync(nameof(Subscription), subscription.Id, token);

    public Task UpdateSubscriptionAsync(Guid id, Guid serviceTypeId, string name, decimal fee, CancellationToken token = default) =>
        _application.UpdateSubscriptionAsync(id, serviceTypeId, name, fee, DateTimeOffset.UtcNow, token);

    private async Task EditAddressAsync()
    {
        if (Address is null) return;
        var name = await _dialogs.PromptAsync(AppText.Get("EditAddress"), AppText.Get("Name"), AppText.Get("Save"), AppText.Get("Cancel"), Address.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var full = await _dialogs.PromptAsync(AppText.Get("EditAddress"), AppText.Get("StreetLocation"), AppText.Get("Save"), AppText.Get("Cancel"), Address.FullAddress ?? string.Empty);
        if (full is null) return;
        try { await UpdateAddressAsync(name, full); await LoadAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("EditAddress"), exception); }
    }

    private async Task ArchiveAsync()
    {
        if (!await _dialogs.ConfirmAsync(AppText.Get("ArchiveAddress"), AppText.Get("ArchiveAddressTree"), AppText.Get("Archive"), AppText.Get("Cancel"))) return;
        try { await ArchiveAsync(CancellationToken.None); await _navigation.GoBackAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("ArchiveAddress"), exception); }
    }

    private async Task AddMeterAsync()
    {
        var type = await ChooseServiceTypeAsync(ServiceTypeCategory.Meter);
        if (type is null) return;
        try
        {
            await CreateMeterAsync(type.Id, type.Name, type.DefaultUnit ?? AppText.Get("DefaultMeterUnit"), MeterDisplayType.Digital, null);
            await LoadAsync();
        }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("NewMeter"), exception); }
    }

    private async Task AddSubscriptionAsync()
    {
        var type = await ChooseServiceTypeAsync(ServiceTypeCategory.Subscription);
        if (type is null) return;
        try { await CreateSubscriptionAsync(type.Id, type.Name, 0m); await LoadAsync(); }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("NewSubscription"), exception); }
    }

    private async Task SubscriptionActionsAsync(SubscriptionSummary summary)
    {
        var subscription = summary.Subscription;
        var action = await _dialogs.ChooseAsync(subscription.Name, AppText.Get("Cancel"), AppText.Get("DeleteSubscription"), AppText.Get("EditNameMonthlyFee"), AppText.Get("PaymentReminderSettings"));
        if (action == AppText.Get("DeleteSubscription"))
        {
            if (await _dialogs.ConfirmAsync(AppText.Get("DeleteSubscription"), AppText.Get("DeleteSubscriptionMessage"), AppText.Get("Delete"), AppText.Get("Cancel")))
                await DeleteSubscriptionAsync(subscription);
        }
        else if (action == AppText.Get("EditNameMonthlyFee"))
        {
            var type = await ChooseServiceTypeAsync(ServiceTypeCategory.Subscription);
            if (type is null) return;
            var name = await _dialogs.PromptAsync(AppText.Get("EditSubscription"), AppText.Get("Name"), AppText.Get("Continue"), AppText.Get("Cancel"), subscription.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            var initialFee = summary.CurrentFee?.ToString(CultureInfo.CurrentCulture) ?? "0";
            var feeText = await _dialogs.PromptAsync(AppText.Get("EditMonthlyFee"), AppText.Get("NewFeeStartsToday"), AppText.Get("Save"), AppText.Get("Cancel"), initialFee, numeric: true);
            if (feeText is null || !NumericInputParser.TryDecimal(feeText, out var fee) || fee < 0)
            {
                await _dialogs.AlertAsync(AppText.Get("InvalidAmount"), AppText.Get("NonNegativeAmount"), AppText.Get("OK"));
                return;
            }
            await UpdateSubscriptionAsync(subscription.Id, type.Id, name, fee);
        }
        else if (action == AppText.Get("PaymentReminderSettings"))
            await _navigation.OpenReminderEditorAsync(ReminderOwnerType.Subscription, subscription.Id, subscription.Name, ReminderKind.Payment);
        await LoadAsync();
    }

    private async Task<ServiceType?> ChooseServiceTypeAsync(ServiceTypeCategory usage)
    {
        var types = (await _application.GetServiceTypesAsync())
            .Where(item => item.IsActive && (item.Category == usage || item.Category == ServiceTypeCategory.Both)).ToArray();
        if (types.Length == 0)
        {
            var message = usage == ServiceTypeCategory.Meter ? "NoMeterServiceTypes" : "NoSubscriptionServiceTypes";
            await _dialogs.AlertAsync(AppText.Get("NoServiceTypes"), AppText.Get(message), AppText.Get("OK"));
            return null;
        }
        var choices = types.Select(ServiceTypePresentation.DisplayName).ToArray();
        var selected = await _dialogs.ChooseAsync(AppText.Get("ChooseServiceType"), AppText.Get("Cancel"), null, choices);
        var selectedIndex = Array.IndexOf(choices, selected);
        return selectedIndex < 0 ? null : types[selectedIndex];
    }
}

public sealed record MeterSummary(Meter Meter, Reading? LatestReading, string ServiceName, string Icon, bool UsesCustomIcon, string IconGlyph)
{
    public bool UsesImageIcon => !UsesCustomIcon;
    public string Title => ServiceName;
    public string Summary => Meter.ReplacedAt.HasValue
        ? AppText.Get("MeterReplacedSummary", Meter.ReplacedAt.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
        : LatestReading is null
            ? AppText.Get("NoReadings")
            : AppText.Get("LatestReading", LatestReading.ConfirmedValue.ToString("0.####", CultureInfo.CurrentCulture), LatestReading.MeasuredAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
}

public sealed record SubscriptionSummary(Subscription Subscription, decimal? CurrentFee, string ServiceName, string Icon, bool UsesCustomIcon, string IconGlyph)
{
    public bool UsesImageIcon => !UsesCustomIcon;
    public string Title => AppText.Get("SubscriptionFeeSummary", ServiceName, CurrentFee?.ToString("0.00", CultureInfo.CurrentCulture) ?? AppText.Get("NoActiveMonthlyFee"));
}
