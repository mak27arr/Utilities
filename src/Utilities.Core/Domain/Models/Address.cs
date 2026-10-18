namespace Utilities.Core.Domain.Models;

public sealed class Address : SoftDeletableEntity
{
    private Address() { }

    public Address(string name, string? fullAddress = null)
    {
        Name = DomainText.Required(name, nameof(name));
        FullAddress = string.IsNullOrWhiteSpace(fullAddress) ? null : fullAddress.Trim();
    }

    public string Name { get; private set; } = string.Empty;
    public string? FullAddress { get; private set; }

    public void Rename(string name) { Name = DomainText.Required(name, nameof(name)); Touch(); }
    public void ChangeFullAddress(string? address) { FullAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim(); Touch(); }
}

internal static class DomainText
{
    public static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
