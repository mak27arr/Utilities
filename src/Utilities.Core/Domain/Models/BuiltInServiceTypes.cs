namespace Utilities.Core.Domain.Models;

public static class BuiltInServiceTypes
{
    public static IReadOnlyList<ServiceType> CreateDefaults() =>
    [
        new("Cold water", isBuiltIn: true, ServiceTypeCategory.Meter, "m³", "service-cold-water"),
        new("Hot water", isBuiltIn: true, ServiceTypeCategory.Meter, "m³", "service-hot-water"),
        new("Water", isBuiltIn: true, ServiceTypeCategory.Meter, "m³", "service-water"),
        new("Gas", isBuiltIn: true, ServiceTypeCategory.Meter, "m³", "service-gas"),
        new("Electricity", isBuiltIn: true, ServiceTypeCategory.Meter, "kWh", "service-electricity"),
        new("Heating", isBuiltIn: true, ServiceTypeCategory.Meter, "Gcal", "service-heating"),
        new("Housing and utilities", isBuiltIn: true, ServiceTypeCategory.Subscription, iconKey: "service-housing"),
        new("Internet", isBuiltIn: true, ServiceTypeCategory.Subscription, iconKey: "service-internet"),
        new("Waste collection", isBuiltIn: true, ServiceTypeCategory.Subscription, iconKey: "service-waste")
    ];
}
