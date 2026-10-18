namespace Utilities.Core.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Utilities.Core.Application.UseCases;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Infrastructure.Persistence;
using Xunit;

public sealed class SqliteApplicationRegressionTests
{
    [Fact]
    public async Task RejectedServiceTypeUpdate_CannotLeakIntoNextUnrelatedSave()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var serviceType = new ServiceType("Water", category: ServiceTypeCategory.Meter, defaultUnit: "m³");
        await app.SeedAsync(serviceType);

        await Assert.ThrowsAsync<ArgumentException>(() => app.Application.UpdateServiceTypeAsync(
            serviceType.Id, "Rejected name", ServiceTypeCategory.Meter, new string('u', 25)));
        await app.Application.CreateAddressAsync("Home", null);

        app.Context.ChangeTracker.Clear();
        var stored = await app.Context.ServiceTypes.AsNoTracking().SingleAsync(item => item.Id == serviceType.Id);
        Assert.Equal("Water", stored.Name);
        Assert.Equal("m³", stored.DefaultUnit);
    }

    [Fact]
    public async Task RejectedMeterAndSubscriptionUpdates_CannotLeakIntoLaterSave()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (address, serviceType, meter) = await app.SeedMeterAsync();
        var otherType = new ServiceType("Gas", category: ServiceTypeCategory.Subscription);
        var subscription = new Subscription(address.Id, serviceType.Id, "Heating");
        var rate = new SubscriptionRate(subscription.Id, 10m, DateTimeOffset.UtcNow.AddMonths(-1));
        await app.SeedAsync(otherType, subscription, rate);
        await app.Application.UpdateMeterIdentifiersAsync(meter.Id, "old-meter", "old-account");

        await Assert.ThrowsAsync<ArgumentException>(() => app.Application.UpdateMeterAsync(
            meter.Id, serviceType.Id, "Renamed", " ", MeterDisplayType.MechanicalRoller));
        await Assert.ThrowsAsync<ArgumentException>(() => app.Application.UpdateMeterIdentifiersAsync(
            meter.Id, "new-meter", new string('a', 81)));
        await Assert.ThrowsAsync<ArgumentException>(() => app.Application.UpdateSubscriptionAsync(
            subscription.Id, otherType.Id, " ", 20m, DateTimeOffset.UtcNow));
        await app.Application.CreateAddressAsync("Unrelated save", null);

        app.Context.ChangeTracker.Clear();
        var storedMeter = await app.Context.Meters.AsNoTracking().SingleAsync(item => item.Id == meter.Id);
        var storedSubscription = await app.Context.Subscriptions.AsNoTracking().SingleAsync(item => item.Id == subscription.Id);
        Assert.Equal("Water", storedMeter.Name);
        Assert.Equal("m³", storedMeter.Unit);
        Assert.Equal("old-meter", storedMeter.MeterNumber);
        Assert.Equal("old-account", storedMeter.AccountNumber);
        Assert.Equal(serviceType.Id, storedSubscription.ServiceTypeId);
        Assert.Equal("Heating", storedSubscription.Name);
    }

    [Fact]
    public async Task LatestHistoryQueries_ReturnOnlyTheNewestActiveReadingAndTariff()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var at = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var oldReading = new Reading(meter.Id, 10, at, null, null, null);
        var newestDeletedReading = new Reading(meter.Id, 20, at.AddDays(1), null, null, null);
        newestDeletedReading.MarkDeleted();
        var oldTariff = new MeterTariffPeriod(meter.Id, 1m, at);
        var newestDeletedTariff = new MeterTariffPeriod(meter.Id, 2m, at.AddDays(1));
        newestDeletedTariff.MarkDeleted();
        await app.SeedAsync(oldReading, newestDeletedReading, oldTariff, newestDeletedTariff);

        Assert.Equal(oldReading.Id, (await app.Store.GetLatestReadingAsync(meter.Id))?.Id);
        Assert.Equal(oldTariff.Id, (await app.Store.GetLatestTariffAsync(meter.Id))?.Id);
    }

    [Fact]
    public async Task CorrectingReading_RecalculatesAllLaterConsumptionAndAmountSnapshots()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var first = new Reading(meter.Id, 100, start, 2m, null, null);
        var edited = new Reading(meter.Id, 120, start.AddMonths(1), 2m, 20, 40m);
        var later = new Reading(meter.Id, 130, start.AddMonths(2), 2m, 10, 20m);
        await app.SeedAsync(first, edited, later);

        await app.Application.UpdateReadingAsync(edited.Id, 110, edited.MeasuredAt);

        app.Context.ChangeTracker.Clear();
        var reloaded = await app.Context.Readings.AsNoTracking().SingleAsync(item => item.Id == later.Id);
        Assert.Equal(20, reloaded.ConsumptionSnapshot);
        Assert.Equal(40m, reloaded.AmountSnapshot);
    }

    [Fact]
    public async Task CorrectingReadingAfterTariffWasDeleted_PreservesAppliedRateSnapshot()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var tariff = new MeterTariffPeriod(meter.Id, 2.5m, start);
        var previous = new Reading(meter.Id, 100, start, null, null, null);
        var reading = new Reading(meter.Id, 120, start.AddMonths(1), 2.5m, 20, 50m);
        await app.SeedAsync(tariff, previous, reading);

        await app.Application.DeleteAsync(nameof(MeterTariffPeriod), tariff.Id);
        await app.Application.UpdateReadingAsync(reading.Id, 121, reading.MeasuredAt);

        app.Context.ChangeTracker.Clear();
        var reloaded = await app.Context.Readings.AsNoTracking().SingleAsync(item => item.Id == reading.Id);
        Assert.Equal(2.5m, reloaded.AppliedRatePerUnit);
        Assert.Equal(52.5m, reloaded.AmountSnapshot);
    }

    [Fact]
    public async Task CsvImport_IsBlockedAfterAllExistingReadingsWereSoftDeleted()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var reading = new Reading(meter.Id, 10, DateTimeOffset.UtcNow.AddDays(-1), null, null, null);
        await app.SeedAsync(reading);
        await app.Application.DeleteAsync(nameof(Reading), reading.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Application.ImportReadingsAsync(meter.Id,
            [new ImportedMeterReading(DateTime.Today.AddDays(-2), 5, 2)]));
        Assert.True(await app.Store.HasAnyReadingAsync(meter.Id, includeDeleted: true));
        Assert.False(await app.Store.HasAnyReadingAsync(meter.Id));
    }

    [Fact]
    public async Task CsvImport_ReportsOverflowingRowAndPersistsNoneOfTheRows()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        await app.SeedAsync(new MeterTariffPeriod(meter.Id, 1m, DateTimeOffset.UtcNow.AddYears(-1)));
        var start = DateTime.Today.AddDays(-2);
        var imported = new[]
        {
            new ImportedMeterReading(start, 1, 2),
            new ImportedMeterReading(start.AddDays(1), 1e300, 3)
        };

        var exception = await Assert.ThrowsAsync<ReadingCsvRowException>(() => app.Application.ImportReadingsAsync(meter.Id, imported));

        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(ReadingCsvErrorCode.CalculationOverflow, exception.ErrorCode);
        Assert.Empty(await app.Context.Readings.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task MeterCanKeepReadingAndPaymentRemindersIndependently()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var readingAt = DateTimeOffset.UtcNow.AddDays(2);
        var paymentAt = DateTimeOffset.UtcNow.AddDays(8);
        var reading = await app.Application.ConfigureReminderAsync(ReminderOwnerType.Meter, meter.Id,
            ReminderKind.ReadingSubmission, true, readingAt, 7, "UTC", 15);
        var payment = await app.Application.ConfigureReminderAsync(ReminderOwnerType.Meter, meter.Id,
            ReminderKind.Payment, true, paymentAt, 30, "UTC", 20);

        await app.Application.ConfigureReminderAsync(ReminderOwnerType.Meter, meter.Id,
            ReminderKind.ReadingSubmission, false, readingAt, 7, "UTC", 15);

        app.Context.ChangeTracker.Clear();
        var reminders = await app.Context.ReminderSettings.AsNoTracking().OrderBy(item => item.Kind).ToArrayAsync();
        Assert.Equal(2, reminders.Length);
        Assert.False(reminders.Single(item => item.Kind == ReminderKind.ReadingSubmission).IsEnabled);
        Assert.True(reminders.Single(item => item.Kind == ReminderKind.Payment).IsEnabled);
        Assert.Equal(payment.NextReminderAt, reminders.Single(item => item.Kind == ReminderKind.Payment).NextReminderAt);
    }

    [Fact]
    public async Task ReplaceMeter_RejectsDatesBeforeSavedReadingsAndTransfersActiveTariff()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var tariff = new MeterTariffPeriod(meter.Id, 2.5m, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var reading = new Reading(meter.Id, 100, new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), 2.5m, null, null);
        await app.SeedAsync(tariff, reading);

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Application.ReplaceMeterAsync(meter.Id, 110, 0,
            new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), confirmLowerReading: true));
        var replacementAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var replacement = await app.Application.ReplaceMeterAsync(meter.Id, 110, 0, replacementAt);

        app.Context.ChangeTracker.Clear();
        var oldTariff = await app.Context.MeterTariffPeriods.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == tariff.Id);
        var newTariff = await app.Context.MeterTariffPeriods.AsNoTracking().SingleAsync(item => item.MeterId == replacement.Id);
        Assert.Equal(replacementAt, oldTariff.EffectiveTo);
        Assert.Equal(2.5m, newTariff.RatePerUnit);
        Assert.Equal(replacementAt, newTariff.EffectiveFrom);
    }

    [Fact]
    public async Task DeletingAndRestoringTariff_KeepsAdjacentPeriodsContinuous()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var jan = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var first = new MeterTariffPeriod(meter.Id, 1m, jan);
        var middle = new MeterTariffPeriod(meter.Id, 2m, jan.AddMonths(1));
        var last = new MeterTariffPeriod(meter.Id, 3m, jan.AddMonths(2));
        await app.SeedAsync(first, middle, last);

        await app.Application.DeleteAsync(nameof(MeterTariffPeriod), middle.Id);
        app.Context.ChangeTracker.Clear();
        var openGapPeriod = await app.Context.MeterTariffPeriods.AsNoTracking().SingleAsync(item => item.Id == first.Id);
        Assert.Equal(last.EffectiveFrom, openGapPeriod.EffectiveTo);

        var deleted = (await app.Application.GetRecentlyDeletedAsync()).Single(item => item.Id == middle.Id);
        await app.Application.RestoreAsync(deleted);
        app.Context.ChangeTracker.Clear();
        var periods = await app.Context.MeterTariffPeriods.AsNoTracking().OrderBy(item => item.EffectiveFrom).ToArrayAsync();
        Assert.Equal(middle.EffectiveFrom, periods[0].EffectiveTo);
        Assert.Equal(last.EffectiveFrom, periods[1].EffectiveTo);
        Assert.Null(periods[2].EffectiveTo);
    }

    [Fact]
    public async Task RestoringTariff_WhenAReplacementPeriodUsesItsStartDate_IsRejectedWithoutOverlap()
    {
        await using var app = await SqliteApplicationHarness.CreateAsync();
        var (_, _, meter) = await app.SeedMeterAsync();
        var first = new MeterTariffPeriod(meter.Id, 1m, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var deletedTariff = new MeterTariffPeriod(meter.Id, 2m, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await app.SeedAsync(first, deletedTariff);
        await app.Application.DeleteAsync(nameof(MeterTariffPeriod), deletedTariff.Id);
        var replacement = await app.Application.CreateTariffAsync(meter.Id, 4m, deletedTariff.EffectiveFrom);

        var deleted = (await app.Application.GetRecentlyDeletedAsync()).Single(item => item.Id == deletedTariff.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Application.RestoreAsync(deleted));

        app.Context.ChangeTracker.Clear();
        var activePeriods = await app.Context.MeterTariffPeriods.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, activePeriods.Length);
        Assert.Single(activePeriods, period => period.EffectiveFrom == deletedTariff.EffectiveFrom);
        Assert.Equal(4m, activePeriods.Single(period => period.EffectiveFrom == deletedTariff.EffectiveFrom).RatePerUnit);
        Assert.Equal(4m, replacement.RatePerUnit);
    }

    internal sealed class NullRecognizer : IReadingRecognizer
    {
        public Task<ReadingRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReadingRecognitionResult(string.Empty, null, null));
    }
}

internal sealed class SqliteApplicationHarness : IAsyncDisposable
{
    private SqliteApplicationHarness(SqliteConnection connection, UtilitiesDbContext context)
    {
        Connection = connection;
        Context = context;
        Store = new EfUtilitiesStore(context);
        Application = new UtilitiesApplicationService(Store, new SqliteApplicationRegressionTests.NullRecognizer());
    }

    public SqliteConnection Connection { get; }
    public UtilitiesDbContext Context { get; }
    public EfUtilitiesStore Store { get; }
    public UtilitiesApplicationService Application { get; }

    public static async Task<SqliteApplicationHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).Options;
        var context = new UtilitiesDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new SqliteApplicationHarness(connection, context);
    }

    public async Task SeedAsync(params SoftDeletableEntity[] entities)
    {
        Context.AddRange(entities);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    public async Task<(Address Address, ServiceType ServiceType, Meter Meter)> SeedMeterAsync()
    {
        var address = new Address("Home");
        var serviceType = new ServiceType("Water", category: ServiceTypeCategory.Meter, defaultUnit: "m³");
        var meter = new Meter(address.Id, serviceType.Id, "Water", "m³", MeterDisplayType.Digital);
        await SeedAsync(address, serviceType, meter);
        return (address, serviceType, meter);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
