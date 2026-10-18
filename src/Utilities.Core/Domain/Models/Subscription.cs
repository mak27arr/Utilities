namespace Utilities.Core.Domain.Models;

public sealed class Subscription : SoftDeletableEntity
{
    private Subscription() { }

    public Subscription(Guid addressId, Guid serviceTypeId, string name)
    {
        if (addressId == Guid.Empty) throw new ArgumentException("An address is required.", nameof(addressId));
        if (serviceTypeId == Guid.Empty) throw new ArgumentException("A service type is required.", nameof(serviceTypeId));
        AddressId = addressId;
        ServiceTypeId = serviceTypeId;
        Name = DomainText.Required(name, nameof(name));
    }

    public Guid AddressId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public void UpdateDetails(Guid serviceTypeId, string name)
    {
        if (serviceTypeId == Guid.Empty) throw new ArgumentException("A service type is required.", nameof(serviceTypeId));
        var normalizedName = DomainText.Required(name, nameof(name));
        ServiceTypeId = serviceTypeId;
        Name = normalizedName;
        Touch();
    }
}
