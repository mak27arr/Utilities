namespace Utilities.Core.Tests;

using Utilities.Core.Application.Abstractions;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Domain.Models;
using Utilities.Core.Infrastructure.Abstractions;
using Xunit;

public sealed class ReadingRecognitionWorkflowTests
{
    [Fact]
    public async Task RecognizeReading_ReturnsSuggestionWithoutPersistingReading()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var store = new InMemoryUtilitiesStore(meter);
        var recognizer = new FixedRecognizer(new ReadingRecognitionResult("123.45", 123.45, 0.93));
        var application = new UtilitiesApplicationService(store, recognizer);

        var result = await application.RecognizeReadingAsync(meter.Id, new byte[] { 1, 2, 3 });

        Assert.Equal(123.45, result.SuggestedValue);
        Assert.Equal("123.45", result.RecognizedText);
        Assert.Empty(await application.GetReadingsAsync(meter.Id));
    }

    [Fact]
    public async Task AddReading_StoresRecognizedTextOnlyAfterConfirmedReadingIsSubmitted()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var store = new InMemoryUtilitiesStore(meter);
        var application = new UtilitiesApplicationService(store, new FixedRecognizer(new ReadingRecognitionResult("12.5", 12.5, null)));
        var at = DateTimeOffset.UtcNow;

        var saved = await application.AddReadingAsync(meter.Id, 12.75, at, recognizedText: "12.5");

        Assert.Equal(12.75, saved.ConfirmedValue);
        Assert.Equal("12.5", saved.RecognizedText);
        Assert.Single(await application.GetReadingsAsync(meter.Id));
    }

    [Fact]
    public async Task AddReading_LowerValueRequiresConfirmationBeforeAnyRecordIsAdded()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var previous = new Reading(meter.Id, 10, DateTimeOffset.UtcNow.AddDays(-1), null, null, null);
        var store = new InMemoryUtilitiesStore(meter, previous);
        var application = new UtilitiesApplicationService(store, new FixedRecognizer(new ReadingRecognitionResult("9", 9, null)));

        await Assert.ThrowsAsync<ReadingReviewRequiredException>(() => application.AddReadingAsync(meter.Id, 9, DateTimeOffset.UtcNow));
        Assert.Single(await application.GetReadingsAsync(meter.Id));
    }

    [Fact]
    public async Task ImportReadings_SavesChronologicalHistoryAndConsumptionSnapshots()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var tariff = new MeterTariffPeriod(meter.Id, 2m, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryUtilitiesStore(meter, tariff);
        var application = new UtilitiesApplicationService(store, new FixedRecognizer(new ReadingRecognitionResult("", null, null)));
        var imported = Utilities.Core.BusinessLogic.ReadingCsvParser.Parse("Date;Reading\n02/03/2025;12,5\n01/03/2025;10");

        var saved = await application.ImportReadingsAsync(meter.Id, imported);

        Assert.Equal(2, saved.Count);
        Assert.Null(saved[0].ConsumptionSnapshot);
        Assert.Equal(2.5, saved[1].ConsumptionSnapshot);
        Assert.Equal(5m, saved[1].AmountSnapshot);
    }

    [Fact]
    public async Task ImportReadings_RequiresReviewForLowerValueWithoutPersistingAnyRows()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var store = new InMemoryUtilitiesStore(meter);
        var application = new UtilitiesApplicationService(store, new FixedRecognizer(new ReadingRecognitionResult("", null, null)));
        var imported = Utilities.Core.BusinessLogic.ReadingCsvParser.Parse("01/03/2025,12\n02/03/2025,9");

        var exception = await Assert.ThrowsAsync<Utilities.Core.BusinessLogic.ReadingCsvRowException>(() => application.ImportReadingsAsync(meter.Id, imported));

        Assert.Equal(2, exception.LineNumber);
        Assert.Equal(Utilities.Core.BusinessLogic.ReadingCsvErrorCode.LowerThanPreviousReading, exception.ErrorCode);
        Assert.Empty(await application.GetReadingsAsync(meter.Id));
    }

    [Fact]
    public async Task ImportReadings_IsUnavailableAfterFirstReading()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var existing = new Reading(meter.Id, 1, DateTimeOffset.UtcNow, null, null, null);
        var application = new UtilitiesApplicationService(new InMemoryUtilitiesStore(meter, existing),
            new FixedRecognizer(new ReadingRecognitionResult("", null, null)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.ImportReadingsAsync(meter.Id,
            [new Utilities.Core.BusinessLogic.ImportedMeterReading(DateTime.Today.AddDays(-1), 2)]));
    }

    [Fact]
    public async Task ReplaceMeter_StoresFinalAndInitialReadingsAndDisablesOldReminder()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var meterReminder = new ReminderSettings(ReminderOwnerType.Meter, meter.Id, ReminderKind.ReadingSubmission,
            true, DateTimeOffset.UtcNow.AddDays(1), 30);
        var store = new InMemoryUtilitiesStore(meter, meterReminder);
        var application = new UtilitiesApplicationService(store, new FixedRecognizer(new ReadingRecognitionResult("", null, null)));
        var at = DateTimeOffset.UtcNow;

        var replacement = await application.ReplaceMeterAsync(meter.Id, 120.5, 0.25, at);

        Assert.Equal(meter.Id, replacement.ReplacesMeterId);
        Assert.Equal(replacement.Id, meter.ReplacedByMeterId);
        Assert.Equal(at, meter.ReplacedAt);
        Assert.Equal(120.5, Assert.Single(await application.GetReadingsAsync(meter.Id)).ConfirmedValue);
        Assert.Single(await application.GetReadingsAsync(replacement.Id));
        Assert.False(meterReminder.IsEnabled);
    }

    [Fact]
    public async Task AddReading_RejectsReadingAfterMeterReplacement()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var replacedAt = DateTimeOffset.UtcNow;
        meter.MarkReplacedBy(Guid.NewGuid(), replacedAt);
        var application = new UtilitiesApplicationService(new InMemoryUtilitiesStore(meter),
            new FixedRecognizer(new ReadingRecognitionResult("", null, null)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.AddReadingAsync(meter.Id, 12,
            replacedAt.AddSeconds(1)));
    }

    [Fact]
    public async Task CreateTariff_AllowsANewRateLaterOnTheSameDay()
    {
        var meter = new Meter(Guid.NewGuid(), Guid.NewGuid(), "Water", "m³", MeterDisplayType.Digital);
        var firstStart = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        var existing = new MeterTariffPeriod(meter.Id, 1.5m, firstStart);
        var store = new InMemoryUtilitiesStore(meter, existing);
        var application = new UtilitiesApplicationService(store, new FixedRecognizer(new ReadingRecognitionResult("", null, null)));
        var replacementStart = firstStart.AddHours(3);

        var updated = await application.CreateTariffAsync(meter.Id, 2m, replacementStart);

        Assert.Equal(replacementStart, existing.EffectiveTo);
        Assert.Equal(replacementStart, updated.EffectiveFrom);
        Assert.Equal(2m, updated.RatePerUnit);
    }

    private sealed class FixedRecognizer(ReadingRecognitionResult result) : IReadingRecognizer
    {
        public Task<ReadingRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class InMemoryUtilitiesStore(params SoftDeletableEntity[] initial) : IUtilitiesStore
    {
        private readonly List<SoftDeletableEntity> _entities = [.. initial];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<T>> ListAsync<T>(Guid? parentId = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
            where T : SoftDeletableEntity
        {
            IEnumerable<T> query = _entities.OfType<T>();
            if (!includeDeleted) query = query.Where(entity => !entity.IsDeleted);
            if (parentId.HasValue)
            {
                var id = parentId.Value;
                query = query.Where(entity => entity switch
                {
                    Reading reading => reading.MeterId == id,
                    MeterTariffPeriod tariff => tariff.MeterId == id,
                    _ => true
                });
            }
            return Task.FromResult<IReadOnlyList<T>>(query.ToArray());
        }

        public Task<T?> FindAsync<T>(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
            where T : SoftDeletableEntity =>
            Task.FromResult(_entities.OfType<T>().FirstOrDefault(entity => entity.Id == id && (includeDeleted || !entity.IsDeleted)));

        public Task<Reading?> GetLatestReadingAsync(Guid meterId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.OfType<Reading>().Where(item => !item.IsDeleted && item.MeterId == meterId)
                .OrderByDescending(item => item.MeasuredAt).ThenByDescending(item => item.Id).FirstOrDefault());

        public Task<MeterTariffPeriod?> GetLatestTariffAsync(Guid meterId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.OfType<MeterTariffPeriod>().Where(item => !item.IsDeleted && item.MeterId == meterId)
                .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.Id).FirstOrDefault());

        public Task<bool> HasAnyReadingAsync(Guid meterId, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.OfType<Reading>().Any(item => item.MeterId == meterId && (includeDeleted || !item.IsDeleted)));

        public Task AddAsync(SoftDeletableEntity entity, CancellationToken cancellationToken = default)
        {
            _entities.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddBatchAtomicallyAsync(IReadOnlyCollection<SoftDeletableEntity> entities, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entities.AddRange(entities);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteTreeAsync(SoftDeletableEntity root, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            root.MarkDeleted(at);
            return Task.CompletedTask;
        }

        public Task RestoreTreeAsync(string entityType, Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            var entity = _entities.First(item => item.Id == id);
            entity.Restore(at);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RecentlyDeletedItem>> GetRecentlyDeletedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecentlyDeletedItem>>([]);

        public Task<IReadOnlyList<ReminderSettings>> GetEnabledRemindersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReminderSettings>>([]);

        public Task ClearAllDataAsync(CancellationToken cancellationToken = default)
        {
            _entities.Clear();
            return Task.CompletedTask;
        }
    }
}
