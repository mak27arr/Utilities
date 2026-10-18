namespace Utilities.Core.Domain.Models;

public sealed class Reading : SoftDeletableEntity
{
    private Reading() { }

    public Reading(Guid meterId, double confirmedValue, DateTimeOffset measuredAt, decimal? appliedRatePerUnit, double? consumptionSnapshot, decimal? amountSnapshot, string? recognizedText = null)
    {
        if (meterId == Guid.Empty) throw new ArgumentException("A meter is required.", nameof(meterId));
        if (!double.IsFinite(confirmedValue) || confirmedValue < 0) throw new ArgumentOutOfRangeException(nameof(confirmedValue));
        if (appliedRatePerUnit is < 0) throw new ArgumentOutOfRangeException(nameof(appliedRatePerUnit));
        if (consumptionSnapshot is < 0 || (consumptionSnapshot.HasValue && !double.IsFinite(consumptionSnapshot.Value))) throw new ArgumentOutOfRangeException(nameof(consumptionSnapshot));
        if (amountSnapshot is < 0) throw new ArgumentOutOfRangeException(nameof(amountSnapshot));
        MeterId = meterId; ConfirmedValue = confirmedValue; MeasuredAt = measuredAt;
        AppliedRatePerUnit = appliedRatePerUnit; ConsumptionSnapshot = consumptionSnapshot; AmountSnapshot = amountSnapshot;
        RecognizedText = string.IsNullOrWhiteSpace(recognizedText) ? null : recognizedText.Trim();
    }
    public Guid MeterId { get; private set; }
    public double ConfirmedValue { get; private set; }
    public DateTimeOffset MeasuredAt { get; private set; }
    public decimal? AppliedRatePerUnit { get; private set; }
    public double? ConsumptionSnapshot { get; private set; }
    public decimal? AmountSnapshot { get; private set; }
    public string? RecognizedText { get; private set; }

    public void Revise(double confirmedValue, DateTimeOffset measuredAt, decimal? appliedRatePerUnit, double? consumptionSnapshot, decimal? amountSnapshot)
    {
        if (!double.IsFinite(confirmedValue) || confirmedValue < 0) throw new ArgumentOutOfRangeException(nameof(confirmedValue));
        if (appliedRatePerUnit is < 0 || amountSnapshot is < 0 || consumptionSnapshot is < 0) throw new ArgumentOutOfRangeException(nameof(appliedRatePerUnit));
        if (consumptionSnapshot.HasValue && !double.IsFinite(consumptionSnapshot.Value)) throw new ArgumentOutOfRangeException(nameof(consumptionSnapshot));
        ConfirmedValue = confirmedValue; MeasuredAt = measuredAt; AppliedRatePerUnit = appliedRatePerUnit;
        ConsumptionSnapshot = consumptionSnapshot; AmountSnapshot = amountSnapshot; Touch();
    }
}
