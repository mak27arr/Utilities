namespace Utilities.Core.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Utilities.Core.Domain.Models;
using Utilities.Infrastructure.Persistence;
using Xunit;

public sealed class SqliteSoftDeleteTests
{
    [Fact]
    public async Task AddBatchAtomically_WhenOneReadingFails_PersistsNoneOfTheBatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).Options;
        await using var db = new UtilitiesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var serviceType = new ServiceType("Water");
        var address = new Address("Home");
        var meter = new Meter(address.Id, serviceType.Id, "Water", "m³", MeterDisplayType.Digital);
        db.AddRange(serviceType, address, meter);
        await db.SaveChangesAsync();

        var valid = new Reading(meter.Id, 10, DateTimeOffset.UtcNow, null, null, null);
        var missingMeter = new Reading(Guid.NewGuid(), 20, DateTimeOffset.UtcNow, null, null, null);
        var store = new EfUtilitiesStore(db);

        await Assert.ThrowsAsync<DbUpdateException>(() => store.AddBatchAtomicallyAsync([valid, missingMeter]));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.Readings.ToListAsync());
    }

    [Fact]
    public async Task DeleteAndRestoreAddress_ChangesRelatedRecordsAsOneRecoverableTree()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).Options;
        await using var db = new UtilitiesDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var serviceType = new ServiceType("Water");
        var address = new Address("Home");
        var meter = new Meter(address.Id, serviceType.Id, "Cold water", "m³", MeterDisplayType.Digital);
        var reading = new Reading(meter.Id, 10.5, DateTimeOffset.UtcNow, null, null, null);
        var tariff = new MeterTariffPeriod(meter.Id, 2.5m, DateTimeOffset.UtcNow.AddDays(-1));
        var subscription = new Subscription(address.Id, serviceType.Id, "Internet");
        var rate = new SubscriptionRate(subscription.Id, 25m, DateTimeOffset.UtcNow.AddDays(-1));
        var meterReminder = new ReminderSettings(ReminderOwnerType.Meter, meter.Id, ReminderKind.ReadingSubmission, true, DateTimeOffset.UtcNow.AddDays(1));
        var subscriptionReminder = new ReminderSettings(ReminderOwnerType.Subscription, subscription.Id, ReminderKind.Payment, true, DateTimeOffset.UtcNow.AddDays(1));
        db.AddRange(serviceType, address, meter, reading, tariff, subscription, rate, meterReminder, subscriptionReminder);
        await db.SaveChangesAsync();

        var store = new EfUtilitiesStore(db);
        var deletedAt = DateTimeOffset.UtcNow;
        await store.DeleteTreeAsync(address, deletedAt);

        Assert.Empty(await db.Addresses.ToListAsync());
        Assert.Empty(await db.Meters.ToListAsync());
        Assert.Empty(await db.Readings.ToListAsync());
        Assert.Empty(await db.MeterTariffPeriods.ToListAsync());
        Assert.Empty(await db.Subscriptions.ToListAsync());
        Assert.Empty(await db.SubscriptionRates.ToListAsync());
        Assert.Empty(await db.ReminderSettings.ToListAsync());
        Assert.Single(await db.ServiceTypes.ToListAsync());

        var deletedItems = await store.GetRecentlyDeletedAsync();
        Assert.Contains(deletedItems, item => item.EntityType == nameof(Address) && item.Id == address.Id);
        Assert.DoesNotContain(deletedItems, item => item.Id == meter.Id || item.Id == reading.Id || item.Id == tariff.Id || item.Id == subscription.Id || item.Id == rate.Id);

        await store.RestoreTreeAsync(nameof(Address), address.Id, deletedAt.AddMinutes(5));

        Assert.Single(await db.Addresses.ToListAsync());
        Assert.Single(await db.Meters.ToListAsync());
        Assert.Single(await db.Readings.ToListAsync());
        Assert.Single(await db.MeterTariffPeriods.ToListAsync());
        Assert.Single(await db.Subscriptions.ToListAsync());
        Assert.Single(await db.SubscriptionRates.ToListAsync());
        Assert.Equal(2, await db.ReminderSettings.CountAsync());
    }

    [Fact]
    public async Task RestoreMeter_IsRejectedUntilItsDeletedAddressIsRestored()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).Options;
        await using var db = new UtilitiesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var serviceType = new ServiceType("Water", category: ServiceTypeCategory.Meter, defaultUnit: "m³");
        var address = new Address("Home");
        var meter = new Meter(address.Id, serviceType.Id, "Water", "m³", MeterDisplayType.Digital);
        var reading = new Reading(meter.Id, 12, DateTimeOffset.UtcNow, null, null, null);
        var reminder = new ReminderSettings(ReminderOwnerType.Meter, meter.Id, ReminderKind.Payment, true, DateTimeOffset.UtcNow.AddDays(1));
        db.AddRange(serviceType, address, meter, reading, reminder);
        await db.SaveChangesAsync();
        var store = new EfUtilitiesStore(db);

        await store.DeleteTreeAsync((await store.FindAsync<Meter>(meter.Id))!, DateTimeOffset.UtcNow);
        await store.DeleteTreeAsync((await store.FindAsync<Address>(address.Id))!, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreTreeAsync(nameof(Meter), meter.Id, DateTimeOffset.UtcNow));
        Assert.Empty(await db.Meters.ToListAsync());
        Assert.Empty(await db.Readings.ToListAsync());
        Assert.Empty(await db.ReminderSettings.ToListAsync());

        await store.RestoreTreeAsync(nameof(Address), address.Id, DateTimeOffset.UtcNow);
        Assert.Single(await db.Addresses.ToListAsync());
        Assert.Empty(await db.Meters.ToListAsync());
        await store.RestoreTreeAsync(nameof(Meter), meter.Id, DateTimeOffset.UtcNow);
        Assert.Single(await db.Meters.ToListAsync());
        Assert.Single(await db.Readings.ToListAsync());
        Assert.Single(await db.ReminderSettings.ToListAsync());
    }
}
