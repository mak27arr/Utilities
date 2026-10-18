namespace Utilities.Core.Domain.Models;

public sealed class ServiceType : SoftDeletableEntity
{
    private ServiceType() { }

    public ServiceType(string name, bool isBuiltIn = false, ServiceTypeCategory category = ServiceTypeCategory.Both, string? defaultUnit = null, string? iconKey = null)
    {
        Name = DomainText.Required(name, nameof(name));
        IsBuiltIn = isBuiltIn;
        ValidateCategory(category);
        Category = category;
        DefaultUnit = NormalizeUnit(defaultUnit);
        IconKey = NormalizeIconKey(iconKey, category);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public bool IsBuiltIn { get; private set; }
    public ServiceTypeCategory Category { get; private set; } = ServiceTypeCategory.Both;
    public string? DefaultUnit { get; private set; }
    public string IconKey { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public void Rename(string name) { Name = DomainText.Required(name, nameof(name)); Touch(); }

    public void UpdateDetails(string name, ServiceTypeCategory category, string? defaultUnit, string? iconKey = null)
    {
        if (IsBuiltIn) throw new InvalidOperationException("Built-in service types cannot be edited.");
        var normalizedName = DomainText.Required(name, nameof(name));
        ValidateCategory(category);
        var normalizedUnit = NormalizeUnit(defaultUnit);
        var normalizedIconKey = NormalizeIconKey(iconKey, category);
        Name = normalizedName;
        Category = category;
        DefaultUnit = normalizedUnit;
        IconKey = normalizedIconKey;
        Touch();
    }

    public void RefreshBuiltInDefinition(ServiceType definition)
    {
        if (!IsBuiltIn || !definition.IsBuiltIn || !string.Equals(Name, definition.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the matching built-in service type definition can be refreshed.");
        Category = definition.Category;
        DefaultUnit = definition.DefaultUnit;
        IconKey = definition.IconKey;
        if (!IsActive) IsActive = true;
    }

    public void SetActive(bool active)
    {
        if (IsBuiltIn && !active) throw new InvalidOperationException("Built-in service types cannot be deactivated.");
        IsActive = active; Touch();
    }

    private static string? NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        var normalized = DomainText.Required(unit, nameof(unit));
        return normalized.Length > 24
            ? throw new ArgumentException("A default unit cannot exceed 24 characters.", nameof(unit))
            : normalized;
    }

    private static string NormalizeIconKey(string? iconKey, ServiceTypeCategory category)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            return category == ServiceTypeCategory.Subscription ? "building" : "drop";
        var normalized = DomainText.Required(iconKey, nameof(iconKey));
        return normalized.Length > 64
            ? throw new ArgumentException("An icon key cannot exceed 64 characters.", nameof(iconKey))
            : normalized;
    }

    private static void ValidateCategory(ServiceTypeCategory category)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
    }
}

public enum ServiceTypeCategory
{
    Meter = 0,
    Subscription = 1,
    Both = 2
}
