namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Xunit;

public sealed class AddressDeletionRulesTests
{
    [Fact]
    public void SoftDeleteAndRestore_OperateOnRelatedTreeAndPreserveIndependentDeletion()
    {
        var at = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
        var address = new Address("Flat");
        var meter = new Meter(address.Id, Guid.NewGuid(), "Cold water", "m³", MeterDisplayType.Digital);
        var reading = new Reading(meter.Id, 12.5, at, null, null, null);
        var independentlyDeleted = new Reading(meter.Id, 2, at.AddDays(-1), null, null, null);
        independentlyDeleted.MarkDeleted(at.AddDays(-1));

        AddressDeletionRules.SoftDelete(address, [meter, reading, independentlyDeleted], at);

        Assert.All(new SoftDeletableEntity[] { address, meter, reading, independentlyDeleted }, item => Assert.True(item.IsDeleted));
        Assert.Equal(address.Id, meter.DeletedById);
        Assert.Equal(nameof(Address), reading.DeletedByType);
        Assert.Equal(at.AddDays(-1), independentlyDeleted.DeletedAt);

        AddressDeletionRules.Restore(address, [meter, reading, independentlyDeleted], at.AddDays(1));

        Assert.False(address.IsDeleted);
        Assert.False(meter.IsDeleted);
        Assert.False(reading.IsDeleted);
        Assert.True(independentlyDeleted.IsDeleted);
    }
}
