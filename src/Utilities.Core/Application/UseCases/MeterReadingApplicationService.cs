namespace Utilities.Core.Application.UseCases;

using Utilities.Core.Application.Abstractions;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Core.Infrastructure.Abstractions;

/// <summary>Owns reading entry, correction, import, recognition, and meter-replacement workflows.</summary>
public sealed class MeterReadingApplicationService(IUtilitiesStore store, IReadingRecognizer readingRecognizer)
{
    public async Task<ReadingAssessment> PreviewReadingAsync(Guid meterId, double value, DateTimeOffset measuredAt,
        Guid? excludeReadingId = null, CancellationToken token = default)
    {
        _ = await Required<Meter>(meterId, token);
        var readings = await store.ListAsync<Reading>(meterId, cancellationToken: token);
        var previous = readings.Where(reading => reading.Id != excludeReadingId && reading.MeasuredAt < measuredAt)
            .OrderByDescending(reading => reading.MeasuredAt).FirstOrDefault();
        var tariffs = await store.ListAsync<MeterTariffPeriod>(meterId, cancellationToken: token);
        var tariff = TariffRules.SelectMeterTariff(measuredAt, tariffs);
        return MeterReadingRules.Assess(value, previous?.ConfirmedValue, tariff?.RatePerUnit);
    }

    public async Task<ReadingRecognitionResult> RecognizeReadingAsync(Guid meterId, ReadOnlyMemory<byte> croppedImage, CancellationToken token = default)
    {
        _ = await Required<Meter>(meterId, token);
        return await readingRecognizer.RecognizeAsync(croppedImage, token);
    }

    public async Task UpdateReadingAsync(Guid readingId, double value, DateTimeOffset measuredAt, bool confirmLowerReading = false, CancellationToken token = default)
    {
        var reading = await Required<Reading>(readingId, token);
        var meter = await Required<Meter>(reading.MeterId, token);
        if (meter.ReplacedAt is { } replacedAt && measuredAt > replacedAt)
            throw new InvalidOperationException("A replaced meter cannot receive readings after its replacement date.");

        var readings = (await store.ListAsync<Reading>(reading.MeterId, cancellationToken: token))
            .OrderBy(item => item.Id == reading.Id ? measuredAt : item.MeasuredAt)
            .ThenBy(item => item.Id).ToArray();
        var tariffs = await store.ListAsync<MeterTariffPeriod>(reading.MeterId, cancellationToken: token);
        var revisions = new List<(Reading Reading, DateTimeOffset At, ReadingAssessment Assessment)>();
        double? previousValue = null;
        foreach (var item in readings)
        {
            var currentValue = item.Id == reading.Id ? value : item.ConfirmedValue;
            var currentAt = item.Id == reading.Id ? measuredAt : item.MeasuredAt;
            var rate = item.Id == reading.Id && measuredAt != reading.MeasuredAt
                ? TariffRules.SelectMeterTariff(measuredAt, tariffs)?.RatePerUnit
                : item.AppliedRatePerUnit;
            var assessment = MeterReadingRules.Assess(currentValue, previousValue, rate);
            if (assessment.RequiresReview)
            {
                if (item.Id == reading.Id && !confirmLowerReading) throw new ReadingReviewRequiredException();
                if (item.Id != reading.Id)
                    throw new InvalidOperationException("This correction would make a later reading lower than its previous reading. Correct the later reading first.");
            }
            revisions.Add((item, currentAt, assessment));
            previousValue = assessment.ConfirmedValue;
        }

        foreach (var revision in revisions)
            revision.Reading.Revise(revision.Assessment.ConfirmedValue, revision.At,
                revision.Assessment.AppliedRatePerUnit, revision.Assessment.Consumption, revision.Assessment.Amount);
        await store.SaveChangesAsync(token);
    }

    public async Task<Reading> AddReadingAsync(Guid meterId, double value, DateTimeOffset measuredAt, bool confirmLowerReading = false,
        CancellationToken token = default, string? recognizedText = null)
    {
        var meter = await Required<Meter>(meterId, token);
        if (meter.ReplacedAt.HasValue && measuredAt > meter.ReplacedAt.Value)
            throw new InvalidOperationException("A replaced meter cannot receive readings after its replacement date.");
        var assessment = await PreviewReadingAsync(meterId, value, measuredAt, token: token);
        if (assessment.RequiresReview && !confirmLowerReading) throw new ReadingReviewRequiredException();
        var reading = new Reading(meterId, assessment.ConfirmedValue, measuredAt, assessment.AppliedRatePerUnit,
            assessment.Consumption, assessment.Amount, recognizedText);
        await store.AddAsync(reading, token);
        await store.SaveChangesAsync(token);
        return reading;
    }

    public async Task<Reading> AddReadingWithAreaAsync(Guid meterId, double value, DateTimeOffset measuredAt,
        NormalizedReadingArea area, bool confirmLowerReading = false, CancellationToken token = default, string? recognizedText = null)
    {
        var meter = await Required<Meter>(meterId, token);
        meter.SetReadingArea(area);
        return await AddReadingAsync(meterId, value, measuredAt, confirmLowerReading, token, recognizedText);
    }

    public async Task<IReadOnlyList<Reading>> ImportReadingsAsync(Guid meterId, IReadOnlyList<ImportedMeterReading> imported,
        CancellationToken token = default)
    {
        var meter = await Required<Meter>(meterId, token);
        if (imported is null || imported.Count == 0) throw new ArgumentException("At least one reading is required.", nameof(imported));
        if ((await store.ListAsync<Reading>(meterId, includeDeleted: true, cancellationToken: token)).Count > 0)
            throw new InvalidOperationException("CSV import is only available before the first reading is saved.");

        var rows = imported.OrderBy(item => item.Date).ToArray();
        var futureRow = rows.FirstOrDefault(item => item.Date.Date > DateTime.Today);
        if (futureRow is not null) throw RowError(futureRow, rows, ReadingCsvErrorCode.FutureDate);
        var duplicateDate = rows.GroupBy(item => item.Date.Date).FirstOrDefault(group => group.Count() > 1);
        if (duplicateDate is not null) throw RowError(duplicateDate.Last(), rows, ReadingCsvErrorCode.DuplicateDate);
        if (meter.ReplacedAt is { } replacedAt)
        {
            var afterReplacement = rows.FirstOrDefault(item => new DateTimeOffset(DateTime.SpecifyKind(item.Date.Date, DateTimeKind.Local)) > replacedAt);
            if (afterReplacement is not null) throw RowError(afterReplacement, rows, ReadingCsvErrorCode.AfterMeterReplacement);
        }

        var tariffs = await store.ListAsync<MeterTariffPeriod>(meterId, cancellationToken: token);
        var readings = new List<Reading>(rows.Length);
        double? previousValue = null;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var lineNumber = LineNumber(row, index + 1);
            if (!double.IsFinite(row.Value) || row.Value < 0)
                throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.InvalidValue);
            var measuredAt = new DateTimeOffset(DateTime.SpecifyKind(row.Date.Date, DateTimeKind.Local));
            var tariff = TariffRules.SelectMeterTariff(measuredAt, tariffs);
            ReadingAssessment assessment;
            try { assessment = MeterReadingRules.Assess(row.Value, previousValue, tariff?.RatePerUnit); }
            catch (OverflowException exception)
            {
                throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.CalculationOverflow, exception.Message);
            }
            if (assessment.RequiresReview)
                throw new ReadingCsvRowException(lineNumber, ReadingCsvErrorCode.LowerThanPreviousReading);
            readings.Add(new Reading(meterId, assessment.ConfirmedValue, measuredAt, assessment.AppliedRatePerUnit,
                assessment.Consumption, assessment.Amount));
            previousValue = assessment.ConfirmedValue;
        }

        await store.AddBatchAtomicallyAsync(readings.Cast<SoftDeletableEntity>().ToArray(), token);
        return readings;
    }

    public async Task<Meter> ReplaceMeterAsync(Guid oldMeterId, double finalReading, double initialReading, DateTimeOffset replacedAt,
        bool confirmLowerReading = false, CancellationToken token = default)
    {
        var oldMeter = await Required<Meter>(oldMeterId, token);
        if (oldMeter.ReplacedAt.HasValue) throw new InvalidOperationException("This meter has already been replaced.");
        var oldReadings = await store.ListAsync<Reading>(oldMeterId, cancellationToken: token);
        if (oldReadings.Any(reading => reading.MeasuredAt > replacedAt))
            throw new InvalidOperationException("The replacement date cannot be earlier than an existing reading.");

        var assessment = await PreviewReadingAsync(oldMeterId, finalReading, replacedAt, token: token);
        if (assessment.RequiresReview && !confirmLowerReading) throw new ReadingReviewRequiredException();
        var newMeter = new Meter(oldMeter.AddressId, oldMeter.ServiceTypeId, oldMeter.Name, oldMeter.Unit, oldMeter.DisplayType);
        newMeter.UpdateIdentifiers(null, oldMeter.AccountNumber);
        newMeter.MarkAsReplacementOf(oldMeter.Id);
        var finalReadingRecord = new Reading(oldMeter.Id, assessment.ConfirmedValue, replacedAt,
            assessment.AppliedRatePerUnit, assessment.Consumption, assessment.Amount);
        var initialReadingRecord = new Reading(newMeter.Id, initialReading, replacedAt, null, null, null);
        var activeTariff = TariffRules.SelectMeterTariff(replacedAt,
            await store.ListAsync<MeterTariffPeriod>(oldMeterId, cancellationToken: token));
        var transferredTariff = activeTariff is null ? null : new MeterTariffPeriod(newMeter.Id, activeTariff.RatePerUnit, replacedAt);
        var oldTariffToClose = activeTariff is not null && activeTariff.EffectiveFrom < replacedAt ? activeTariff : null;
        var reminders = (await store.ListAsync<ReminderSettings>(oldMeter.Id, cancellationToken: token))
            .Where(item => item.OwnerType == ReminderOwnerType.Meter && item.OwnerId == oldMeter.Id && item.IsEnabled).ToArray();

        oldMeter.MarkReplacedBy(newMeter.Id, replacedAt);
        oldTariffToClose?.CloseAt(replacedAt);
        foreach (var reminder in reminders)
            reminder.Configure(false, reminder.NextReminderAt, reminder.RepeatIntervalDays, reminder.TimeZoneId, reminder.CalendarDayOfMonth);
        await store.AddAsync(newMeter, token);
        await store.AddAsync(finalReadingRecord, token);
        await store.AddAsync(initialReadingRecord, token);
        if (transferredTariff is not null) await store.AddAsync(transferredTariff, token);
        await store.SaveChangesAsync(token);
        return newMeter;
    }

    private static ReadingCsvRowException RowError(ImportedMeterReading reading, IReadOnlyList<ImportedMeterReading> sortedRows,
        ReadingCsvErrorCode error) => new(LineNumber(reading, Array.IndexOf(sortedRows.ToArray(), reading) + 1), error);

    private static int LineNumber(ImportedMeterReading reading, int fallback) => reading.LineNumber > 0 ? reading.LineNumber : fallback;

    private async Task<T> Required<T>(Guid id, CancellationToken token) where T : SoftDeletableEntity =>
        await store.FindAsync<T>(id, cancellationToken: token) ?? throw new KeyNotFoundException($"{typeof(T).Name} was not found.");
}
