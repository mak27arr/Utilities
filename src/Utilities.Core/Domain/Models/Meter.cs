namespace Utilities.Core.Domain.Models;

using Utilities.Core.Domain.ValueObjects;

public enum MeterDisplayType { Digital, MechanicalRoller }

public sealed class Meter : SoftDeletableEntity
{
    private Meter() { }

    public Meter(Guid addressId, Guid serviceTypeId, string name, string unit, MeterDisplayType displayType, NormalizedReadingArea? readingArea = null)
    {
        if (addressId == Guid.Empty) throw new ArgumentException("An address is required.", nameof(addressId));
        if (serviceTypeId == Guid.Empty) throw new ArgumentException("A service type is required.", nameof(serviceTypeId));
        AddressId = addressId;
        ServiceTypeId = serviceTypeId;
        Name = DomainText.Required(name, nameof(name));
        Unit = DomainText.Required(unit, nameof(unit));
        DisplayType = displayType;
        ReadingArea = readingArea;
    }

    public Guid AddressId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public MeterDisplayType DisplayType { get; private set; }
    public NormalizedReadingArea? ReadingArea { get; private set; }
    public string? MeterNumber { get; private set; }
    public string? AccountNumber { get; private set; }
    public DateTimeOffset? ReplacedAt { get; private set; }
    public Guid? ReplacedByMeterId { get; private set; }
    public Guid? ReplacesMeterId { get; private set; }

    public void SetReadingArea(NormalizedReadingArea? readingArea) { ReadingArea = readingArea; Touch(); }

    public void UpdateIdentifiers(string? meterNumber, string? accountNumber)
    {
        var normalizedMeterNumber = NormalizeIdentifier(meterNumber, nameof(meterNumber));
        var normalizedAccountNumber = NormalizeIdentifier(accountNumber, nameof(accountNumber));
        MeterNumber = normalizedMeterNumber;
        AccountNumber = normalizedAccountNumber;
        Touch();
    }

    public void MarkReplacedBy(Guid newMeterId, DateTimeOffset replacedAt)
    {
        if (newMeterId == Guid.Empty || newMeterId == Id)
            throw new ArgumentException("A replacement must link two different meters.");
        if (ReplacedAt.HasValue || ReplacedByMeterId.HasValue)
            throw new InvalidOperationException("This meter has already been replaced.");
        ReplacedAt = replacedAt;
        ReplacedByMeterId = newMeterId;
        Touch();
    }

    public void MarkAsReplacementOf(Guid oldMeterId)
    {
        if (oldMeterId == Guid.Empty || oldMeterId == Id)
            throw new ArgumentException("A replacement must link two different meters.", nameof(oldMeterId));
        ReplacesMeterId = oldMeterId;
        Touch();
    }

    public void UpdateDetails(Guid serviceTypeId, string name, string unit, MeterDisplayType displayType)
    {
        if (serviceTypeId == Guid.Empty) throw new ArgumentException("A service type is required.", nameof(serviceTypeId));
        if (serviceTypeId != ServiceTypeId)
            throw new InvalidOperationException("A meter's service type cannot be changed. Create a replacement meter instead.");
        var normalizedName = DomainText.Required(name, nameof(name));
        var normalizedUnit = DomainText.Required(unit, nameof(unit));
        Name = normalizedName;
        Unit = normalizedUnit;
        DisplayType = displayType;
        Touch();
    }

    private static string? NormalizeIdentifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 80) throw new ArgumentException("An identifier cannot exceed 80 characters.", parameterName);
        return normalized;
    }
}
