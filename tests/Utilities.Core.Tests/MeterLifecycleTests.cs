namespace Utilities.Core.Tests;

using Utilities.Core.Domain.Models;
using Xunit;

public sealed class MeterLifecycleTests
{
    [Fact]
    public void UpdateDetails_RejectsChangingServiceType()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Gas", "m³", MeterDisplayType.Digital);

        Assert.Throws<InvalidOperationException>(() => meter.UpdateDetails(Guid.NewGuid(), "Electricity", "kWh", MeterDisplayType.Digital));
        Assert.Equal("Gas", meter.Name);
    }

    [Fact]
    public void ReplacementLinksOldAndNewMetersAndClosesOldMeter()
    {
        var oldMeter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var newMeter = new Meter(oldMeter.AddressId, oldMeter.ServiceTypeId, oldMeter.Name, oldMeter.Unit, oldMeter.DisplayType);
        var replacedAt = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        newMeter.MarkAsReplacementOf(oldMeter.Id);
        oldMeter.MarkReplacedBy(newMeter.Id, replacedAt);

        Assert.Equal(newMeter.Id, oldMeter.ReplacedByMeterId);
        Assert.Equal(replacedAt, oldMeter.ReplacedAt);
        Assert.Equal(oldMeter.Id, newMeter.ReplacesMeterId);
        Assert.Throws<InvalidOperationException>(() => oldMeter.MarkReplacedBy(Guid.NewGuid(), replacedAt.AddDays(1)));
    }

    [Fact]
    public void FailedMeterUpdates_DoNotPartiallyMutateTrackedValues()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        meter.UpdateIdentifiers("old-meter", "old-account");

        Assert.Throws<ArgumentException>(() => meter.UpdateDetails(meter.ServiceTypeId, "Renamed", " ", MeterDisplayType.MechanicalRoller));
        Assert.Throws<ArgumentException>(() => meter.UpdateIdentifiers("new-meter", new string('a', 81)));

        Assert.Equal("Water", meter.Name);
        Assert.Equal("m³", meter.Unit);
        Assert.Equal("old-meter", meter.MeterNumber);
        Assert.Equal("old-account", meter.AccountNumber);
    }
}
