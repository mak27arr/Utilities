namespace Utilities.Core.Application.UseCases;

using Utilities.Core.Application.Abstractions;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Core.Infrastructure.Abstractions;

public sealed class ReadingReviewRequiredException : Exception
{
    public ReadingReviewRequiredException() : base("The reading is lower than the previous reading and requires confirmation.") { }
}

public sealed class UtilitiesApplicationService(IUtilitiesStore store, IReadingRecognizer readingRecognizer)
{
    private readonly MeterReadingApplicationService readingWorkflows = new(store, readingRecognizer);

    public Task InitializeAsync(CancellationToken token = default) => store.InitializeAsync(token);

    public Task<IReadOnlyList<Address>> GetAddressesAsync(CancellationToken token = default) => store.ListAsync<Address>(cancellationToken: token);
    public Task<Address?> GetAddressAsync(Guid id, CancellationToken token = default) => store.FindAsync<Address>(id, cancellationToken: token);
    public Task<Meter?> GetMeterAsync(Guid id, CancellationToken token = default) => store.FindAsync<Meter>(id, cancellationToken: token);
    public Task<Subscription?> GetSubscriptionAsync(Guid id, CancellationToken token = default) => store.FindAsync<Subscription>(id, cancellationToken: token);
    public Task<IReadOnlyList<SubscriptionRate>> GetSubscriptionRatesAsync(Guid subscriptionId, CancellationToken token = default) => store.ListAsync<SubscriptionRate>(subscriptionId, cancellationToken: token);
    public Task<IReadOnlyList<Meter>> GetMetersAsync(Guid addressId, CancellationToken token = default) => store.ListAsync<Meter>(addressId, cancellationToken: token);
    public Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(Guid addressId, CancellationToken token = default) => store.ListAsync<Subscription>(addressId, cancellationToken: token);
    public Task<IReadOnlyList<ServiceType>> GetServiceTypesAsync(CancellationToken token = default) => store.ListAsync<ServiceType>(cancellationToken: token);
    public Task<ServiceType?> GetServiceTypeAsync(Guid id, CancellationToken token = default) => store.FindAsync<ServiceType>(id, cancellationToken: token);
    public Task<IReadOnlyList<Reading>> GetReadingsAsync(Guid meterId, CancellationToken token = default) => store.ListAsync<Reading>(meterId, cancellationToken: token);
    public Task<Reading?> GetLatestReadingAsync(Guid meterId, CancellationToken token = default) => store.GetLatestReadingAsync(meterId, token);
    public Task<MeterTariffPeriod?> GetLatestTariffAsync(Guid meterId, CancellationToken token = default) => store.GetLatestTariffAsync(meterId, token);
    public Task<bool> HasAnyReadingAsync(Guid meterId, bool includeDeleted = false, CancellationToken token = default) =>
        store.HasAnyReadingAsync(meterId, includeDeleted, token);
    public Task<IReadOnlyList<MeterTariffPeriod>> GetTariffsAsync(Guid meterId, CancellationToken token = default) => store.ListAsync<MeterTariffPeriod>(meterId, cancellationToken: token);
    public Task<IReadOnlyList<RecentlyDeletedItem>> GetRecentlyDeletedAsync(CancellationToken token = default) => store.GetRecentlyDeletedAsync(token);
    public Task<IReadOnlyList<ReminderSettings>> GetRemindersAsync(Guid ownerId, CancellationToken token = default) => store.ListAsync<ReminderSettings>(ownerId, cancellationToken: token);
    public Task<IReadOnlyList<ReminderSettings>> GetEnabledRemindersAsync(CancellationToken token = default) => store.GetEnabledRemindersAsync(token);
    public Task<IReadOnlyList<ReminderSettings>> GetAllRemindersAsync(bool includeDeleted = false, CancellationToken token = default) =>
        store.ListAsync<ReminderSettings>(includeDeleted: includeDeleted, cancellationToken: token);
    public Task<ReminderSettings?> GetReminderAsync(Guid id, CancellationToken token = default) => store.FindAsync<ReminderSettings>(id, cancellationToken: token);

    public async Task<ReminderSettings> ConfigureReminderAsync(ReminderOwnerType ownerType, Guid ownerId, ReminderKind kind, bool enabled, DateTimeOffset? nextReminderAt, int? repeatIntervalDays, string? timeZoneId = null, int? calendarDayOfMonth = null, CancellationToken token = default)
    {
        if (ownerType == ReminderOwnerType.Meter) _ = await Required<Meter>(ownerId, token);
        else _ = await Required<Subscription>(ownerId, token);
        var existing = (await store.ListAsync<ReminderSettings>(ownerId, cancellationToken: token)).SingleOrDefault(item => item.OwnerType == ownerType && item.Kind == kind);
        if (existing is null)
        {
            existing = new ReminderSettings(ownerType, ownerId, kind, enabled, nextReminderAt, repeatIntervalDays, timeZoneId, calendarDayOfMonth);
            await store.AddAsync(existing, token);
        }
        else existing.Configure(enabled, nextReminderAt, repeatIntervalDays, timeZoneId, calendarDayOfMonth);
        await store.SaveChangesAsync(token);
        return existing;
    }

    public async Task<Address> CreateAddressAsync(string name, string? fullAddress, CancellationToken token = default)
    {
        var address = new Address(name, fullAddress);
        await store.AddAsync(address, token);
        await store.SaveChangesAsync(token);
        return address;
    }

    public async Task UpdateAddressAsync(Guid id, string name, string? fullAddress, CancellationToken token = default)
    {
        var address = await Required<Address>(id, token);
        address.Rename(name);
        address.ChangeFullAddress(fullAddress);
        await store.SaveChangesAsync(token);
    }

    public async Task<Meter> CreateMeterAsync(Guid addressId, Guid serviceTypeId, string name, string unit, MeterDisplayType displayType, double? initialReading, CancellationToken token = default)
    {
        _ = await Required<Address>(addressId, token);
        var serviceType = await Required<ServiceType>(serviceTypeId, token);
        if (!serviceType.IsActive) throw new InvalidOperationException("The selected service type is inactive.");
        var meter = new Meter(addressId, serviceTypeId, name, unit, displayType);
        var initial = initialReading.HasValue
            ? new Reading(meter.Id, initialReading.Value, DateTimeOffset.UtcNow, null, null, null)
            : null;
        await store.AddAsync(meter, token);
        if (initial is not null) await store.AddAsync(initial, token);
        await store.SaveChangesAsync(token);
        return meter;
    }

    public async Task UpdateMeterAsync(Guid id, Guid serviceTypeId, string name, string unit, MeterDisplayType displayType, CancellationToken token = default)
    {
        var meter = await Required<Meter>(id, token);
        var serviceType = await Required<ServiceType>(serviceTypeId, token);
        if (!serviceType.IsActive) throw new InvalidOperationException("The selected service type is inactive.");
        meter.UpdateDetails(serviceTypeId, name, unit, displayType);
        await store.SaveChangesAsync(token);
    }

    public async Task UpdateMeterIdentifiersAsync(Guid id, string? meterNumber, string? accountNumber, CancellationToken token = default)
    {
        var meter = await Required<Meter>(id, token);
        meter.UpdateIdentifiers(meterNumber, accountNumber);
        await store.SaveChangesAsync(token);
    }

    public async Task SetReadingAreaAsync(Guid meterId, NormalizedReadingArea? area, CancellationToken token = default)
    {
        var meter = await Required<Meter>(meterId, token);
        meter.SetReadingArea(area);
        await store.SaveChangesAsync(token);
    }

    public async Task<Subscription> CreateSubscriptionAsync(Guid addressId, Guid serviceTypeId, string name, decimal monthlyFee, DateTimeOffset effectiveFrom, CancellationToken token = default)
    {
        _ = await Required<Address>(addressId, token);
        var serviceType = await Required<ServiceType>(serviceTypeId, token);
        if (!serviceType.IsActive) throw new InvalidOperationException("The selected service type is inactive.");
        var subscription = new Subscription(addressId, serviceTypeId, name);
        var initialRate = new SubscriptionRate(subscription.Id, monthlyFee, effectiveFrom);
        await store.AddAsync(subscription, token);
        await store.AddAsync(initialRate, token);
        await store.SaveChangesAsync(token);
        return subscription;
    }

    public async Task UpdateSubscriptionAsync(Guid id, Guid serviceTypeId, string name, decimal monthlyFee, DateTimeOffset effectiveFrom, CancellationToken token = default)
    {
        var subscription = await Required<Subscription>(id, token);
        var serviceType = await Required<ServiceType>(serviceTypeId, token);
        if (!serviceType.IsActive) throw new InvalidOperationException("The selected service type is inactive.");
        var rates = await store.ListAsync<SubscriptionRate>(id, cancellationToken: token);
        if (rates.Any(rate => rate.EffectiveFrom >= effectiveFrom))
            throw new InvalidOperationException("A subscription rate already starts on or after this date.");
        var newRate = new SubscriptionRate(id, monthlyFee, effectiveFrom);
        subscription.UpdateDetails(serviceTypeId, name);
        rates.FirstOrDefault(rate => !rate.EffectiveTo.HasValue)?.CloseAt(effectiveFrom);
        await store.AddAsync(newRate, token);
        await store.SaveChangesAsync(token);
    }

    public async Task<ServiceType> CreateServiceTypeAsync(string name, ServiceTypeCategory category, string? defaultUnit = null, CancellationToken token = default, string? iconKey = null)
    {
        var serviceType = new ServiceType(name, category: category, defaultUnit: defaultUnit, iconKey: iconKey);
        await store.AddAsync(serviceType, token);
        await store.SaveChangesAsync(token);
        return serviceType;
    }

    public async Task UpdateServiceTypeAsync(Guid id, string name, ServiceTypeCategory category, string? defaultUnit = null, CancellationToken token = default, string? iconKey = null)
    {
        var serviceType = await Required<ServiceType>(id, token);
        serviceType.UpdateDetails(name, category, defaultUnit, iconKey);
        await store.SaveChangesAsync(token);
    }

    public async Task ActivateServiceTypeAsync(Guid id, CancellationToken token = default)
    {
        var serviceType = await Required<ServiceType>(id, token);
        serviceType.SetActive(true);
        await store.SaveChangesAsync(token);
    }

    public async Task DeactivateServiceTypeAsync(Guid id, CancellationToken token = default)
    {
        var serviceType = await Required<ServiceType>(id, token);
        serviceType.SetActive(false);
        await store.SaveChangesAsync(token);
    }

    public async Task<MeterTariffPeriod> CreateTariffAsync(Guid meterId, decimal ratePerUnit, DateTimeOffset effectiveFrom, CancellationToken token = default)
    {
        _ = await Required<Meter>(meterId, token);
        var tariffs = await store.ListAsync<MeterTariffPeriod>(meterId, cancellationToken: token);
        if (tariffs.Any(tariff => tariff.EffectiveFrom >= effectiveFrom))
            throw new InvalidOperationException("A tariff period already starts on or after this date.");
        var tariff = new MeterTariffPeriod(meterId, ratePerUnit, effectiveFrom);
        var openPeriod = tariffs.FirstOrDefault(tariff => !tariff.EffectiveTo.HasValue);
        openPeriod?.CloseAt(effectiveFrom);
        await store.AddAsync(tariff, token);
        await store.SaveChangesAsync(token);
        return tariff;
    }

    public Task<ReadingAssessment> PreviewReadingAsync(Guid meterId, double value, DateTimeOffset measuredAt, Guid? excludeReadingId = null, CancellationToken token = default) =>
        readingWorkflows.PreviewReadingAsync(meterId, value, measuredAt, excludeReadingId, token);

    public Task<ReadingRecognitionResult> RecognizeReadingAsync(Guid meterId, ReadOnlyMemory<byte> croppedImage, CancellationToken token = default) =>
        readingWorkflows.RecognizeReadingAsync(meterId, croppedImage, token);

    public Task UpdateReadingAsync(Guid readingId, double value, DateTimeOffset measuredAt, bool confirmLowerReading = false, CancellationToken token = default) =>
        readingWorkflows.UpdateReadingAsync(readingId, value, measuredAt, confirmLowerReading, token);

    public Task<Reading> AddReadingAsync(Guid meterId, double value, DateTimeOffset measuredAt, bool confirmLowerReading = false,
        CancellationToken token = default, string? recognizedText = null) =>
        readingWorkflows.AddReadingAsync(meterId, value, measuredAt, confirmLowerReading, token, recognizedText);

    public Task<Reading> AddReadingWithAreaAsync(Guid meterId, double value, DateTimeOffset measuredAt,
        NormalizedReadingArea area, bool confirmLowerReading = false, CancellationToken token = default, string? recognizedText = null) =>
        readingWorkflows.AddReadingWithAreaAsync(meterId, value, measuredAt, area, confirmLowerReading, token, recognizedText);

    public Task<IReadOnlyList<Reading>> ImportReadingsAsync(Guid meterId, IReadOnlyList<ImportedMeterReading> imported, CancellationToken token = default) =>
        readingWorkflows.ImportReadingsAsync(meterId, imported, token);

    public Task<Meter> ReplaceMeterAsync(Guid oldMeterId, double finalReading, double initialReading, DateTimeOffset replacedAt,
        bool confirmLowerReading = false, CancellationToken token = default) =>
        readingWorkflows.ReplaceMeterAsync(oldMeterId, finalReading, initialReading, replacedAt, confirmLowerReading, token);
    public async Task DeleteAsync(string entityType, Guid id, CancellationToken token = default)
    {
        SoftDeletableEntity entity = entityType switch
        {
            nameof(Address) => (SoftDeletableEntity)await Required<Address>(id, token),
            nameof(Meter) => (SoftDeletableEntity)await Required<Meter>(id, token),
            nameof(Subscription) => (SoftDeletableEntity)await Required<Subscription>(id, token),
            nameof(Reading) => (SoftDeletableEntity)await Required<Reading>(id, token),
            nameof(MeterTariffPeriod) => (SoftDeletableEntity)await Required<MeterTariffPeriod>(id, token),
            nameof(SubscriptionRate) => (SoftDeletableEntity)await Required<SubscriptionRate>(id, token),
            nameof(ServiceType) => (SoftDeletableEntity)await Required<ServiceType>(id, token),
            nameof(ReminderSettings) => (SoftDeletableEntity)await Required<ReminderSettings>(id, token),
            _ => throw new ArgumentOutOfRangeException(nameof(entityType))
        };

        if (entity is MeterTariffPeriod tariff)
        {
            var periods = await store.ListAsync<MeterTariffPeriod>(tariff.MeterId, cancellationToken: token);
            var next = periods.Where(item => item.Id != tariff.Id && item.EffectiveFrom > tariff.EffectiveFrom)
                .OrderBy(item => item.EffectiveFrom).FirstOrDefault();
            var previous = periods.Where(item => item.Id != tariff.Id && item.EffectiveFrom < tariff.EffectiveFrom)
                .OrderByDescending(item => item.EffectiveFrom).FirstOrDefault();
            previous?.SetEffectiveTo(next?.EffectiveFrom);
        }
        await store.DeleteTreeAsync(entity, DateTimeOffset.UtcNow, token);
    }

    public async Task RestoreAsync(RecentlyDeletedItem item, CancellationToken token = default)
    {
        if (item.EntityType == nameof(MeterTariffPeriod))
        {
            var tariff = await store.FindAsync<MeterTariffPeriod>(item.Id, includeDeleted: true, cancellationToken: token)
                ?? throw new KeyNotFoundException("MeterTariffPeriod was not found.");
            var periods = await store.ListAsync<MeterTariffPeriod>(tariff.MeterId, cancellationToken: token);
            if (periods.Any(period => period.EffectiveFrom == tariff.EffectiveFrom))
                throw new InvalidOperationException("A tariff already exists at this start date. Delete it before restoring this period.");
            var later = periods.Where(period => period.EffectiveFrom > tariff.EffectiveFrom)
                .OrderBy(period => period.EffectiveFrom).FirstOrDefault();
            var previous = periods.Where(period => period.EffectiveFrom < tariff.EffectiveFrom)
                .OrderByDescending(period => period.EffectiveFrom).FirstOrDefault();
            previous?.SetEffectiveTo(tariff.EffectiveFrom);
            tariff.SetEffectiveTo(later?.EffectiveFrom);
        }
        await store.RestoreTreeAsync(item.EntityType, item.Id, DateTimeOffset.UtcNow, token);
    }

    public Task ClearAllDataAsync(CancellationToken token = default) => store.ClearAllDataAsync(token);

    private async Task<T> Required<T>(Guid id, CancellationToken token) where T : SoftDeletableEntity =>
        await store.FindAsync<T>(id, cancellationToken: token) ?? throw new KeyNotFoundException($"{typeof(T).Name} was not found.");
}
