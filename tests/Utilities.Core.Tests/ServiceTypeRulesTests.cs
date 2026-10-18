namespace Utilities.Core.Tests;

using Utilities.Core.Domain.Models;
using Xunit;

public sealed class ServiceTypeRulesTests
{
    [Fact]
    public void BuiltInCatalogIncludesGasAndHousingUtilitiesWithSuitableCategories()
    {
        var defaults = BuiltInServiceTypes.CreateDefaults();

        Assert.Contains(defaults, item => item.Name == "Gas" && item.Category == ServiceTypeCategory.Meter && item.DefaultUnit == "m³");
        Assert.Contains(defaults, item => item.Name == "Housing and utilities" && item.Category == ServiceTypeCategory.Subscription);
        Assert.Contains(defaults, item => item.Name == "Cold water" && item.DefaultUnit == "m³");
        Assert.Contains(defaults, item => item.Name == "Electricity" && item.DefaultUnit == "kWh");
    }

    [Fact]
    public void CustomServiceTypeStoresCategoryAndNormalizedUnit()
    {
        var serviceType = new ServiceType("  Building maintenance  ", category: ServiceTypeCategory.Meter, defaultUnit: "  GJ  ");

        Assert.Equal("Building maintenance", serviceType.Name);
        Assert.Equal(ServiceTypeCategory.Meter, serviceType.Category);
        Assert.Equal("GJ", serviceType.DefaultUnit);
    }

    [Fact]
    public void CustomServiceTypeStoresAndUpdatesItsIconPresetKey()
    {
        var serviceType = new ServiceType("Garden care", category: ServiceTypeCategory.Subscription, iconKey: "garden");

        Assert.Equal("garden", serviceType.IconKey);

        serviceType.UpdateDetails("Garden care", ServiceTypeCategory.Both, "m³", "leaf");

        Assert.Equal("leaf", serviceType.IconKey);
    }

    [Fact]
    public void BuiltInServiceTypesHaveDistinctServiceSpecificIconKeys()
    {
        var defaults = BuiltInServiceTypes.CreateDefaults();

        Assert.Equal(defaults.Count, defaults.Select(item => item.IconKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("service-electricity", Assert.Single(defaults, item => item.Name == "Electricity").IconKey);
        Assert.Equal("service-gas", Assert.Single(defaults, item => item.Name == "Gas").IconKey);
    }

    [Fact]
    public void BuiltInDefinitionCanRefreshCategoryAndUnitWithoutChangingName()
    {
        var existing = new ServiceType("Gas", isBuiltIn: true);
        existing.RefreshBuiltInDefinition(new ServiceType("Gas", isBuiltIn: true, ServiceTypeCategory.Meter, "m³", "service-gas"));

        Assert.Equal(ServiceTypeCategory.Meter, existing.Category);
        Assert.Equal("m³", existing.DefaultUnit);
        Assert.Equal("service-gas", existing.IconKey);
    }

    [Fact]
    public void FailedSubscriptionUpdateDoesNotChangeItsServiceType()
    {
        var address = new Address("Home");
        var firstType = new ServiceType("Water");
        var secondType = new ServiceType("Gas");
        var subscription = new Subscription(address.Id, firstType.Id, "Heating");

        Assert.Throws<ArgumentException>(() => subscription.UpdateDetails(secondType.Id, " "));

        Assert.Equal(firstType.Id, subscription.ServiceTypeId);
        Assert.Equal("Heating", subscription.Name);
    }
}
