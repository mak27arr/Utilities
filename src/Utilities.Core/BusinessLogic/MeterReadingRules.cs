namespace Utilities.Core.BusinessLogic;

public enum ReadingWarning { LowerThanPreviousReading }

public sealed record ReadingAssessment(double ConfirmedValue, double? Consumption, decimal? AppliedRatePerUnit, decimal? Amount, ReadingWarning? Warning)
{
    public bool RequiresReview => Warning.HasValue;
}

public static class MeterReadingRules
{
    public static ReadingAssessment Assess(double currentValue, double? previousValue, decimal? ratePerUnit)
    {
        if (!double.IsFinite(currentValue) || currentValue < 0) throw new ArgumentOutOfRangeException(nameof(currentValue));
        if (previousValue.HasValue && (!double.IsFinite(previousValue.Value) || previousValue.Value < 0)) throw new ArgumentOutOfRangeException(nameof(previousValue));
        if (ratePerUnit is < 0) throw new ArgumentOutOfRangeException(nameof(ratePerUnit));

        if (!previousValue.HasValue)
            return new(currentValue, null, ratePerUnit, null, null);
        if (currentValue < previousValue.Value)
            return new(currentValue, null, ratePerUnit, null, ReadingWarning.LowerThanPreviousReading);
        if (!ratePerUnit.HasValue)
            return new(currentValue, currentValue - previousValue.Value, null, null, null);

        var consumption = currentValue - previousValue.Value;
        var amount = (decimal)consumption * ratePerUnit.Value;
        return new(currentValue, consumption, ratePerUnit, amount, null);
    }
}
