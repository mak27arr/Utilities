namespace Utilities.Mobile.Services;

using Utilities.Core.Domain.Models;

public static class ServiceTypePresentation
{
    public static string DisplayName(ServiceType serviceType)
        => DisplayName(serviceType.Name);

    public static string DisplayName(string name)
    {
        var key = name switch
        {
            "Cold water" => "ServiceColdWater",
            "Hot water" => "ServiceHotWater",
            "Water" => "ServiceWater",
            "Gas" => "ServiceGas",
            "Electricity" => "ServiceElectricity",
            "Heating" => "ServiceHeating",
            "Housing and utilities" => "ServiceHousingUtilities",
            "Internet" => "ServiceInternet",
            "Waste collection" => "ServiceWasteCollection",
            _ => null
        };
        return key is null ? name : AppText.Get(key);
    }

    public static string CategoryName(ServiceTypeCategory category) => AppText.Get(category switch
    {
        ServiceTypeCategory.Meter => "MeterBased",
        ServiceTypeCategory.Subscription => "FixedSubscription",
        _ => "MeterAndSubscription"
    });

    public static string Icon(ServiceType serviceType) => serviceType.Name switch
    {
        "Cold water" => "ic_service_cold_water.png",
        "Hot water" => "ic_service_hot_water.png",
        "Water" => "ic_service_water.png",
        "Gas" => "ic_service_gas.png",
        "Electricity" => "ic_service_electricity.png",
        "Heating" => "ic_service_heating.png",
        "Housing and utilities" => "ic_service_housing.png",
        "Internet" => "ic_service_internet.png",
        "Waste collection" => "ic_service_waste.png",
        _ => Icon(serviceType.Category)
    };

    public static string Glyph(ServiceType serviceType) => ServiceIconPresets.GlyphForKey(serviceType.IconKey);

    public static string Icon(ServiceTypeCategory category) => category switch
    {
        ServiceTypeCategory.Meter => "ic_meter.png",
        ServiceTypeCategory.Subscription => "ic_subscription.png",
        _ => "ic_services.png"
    };
}
