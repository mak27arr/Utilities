namespace Utilities.Core.Domain.Models;

public sealed class MeterTariffPeriod : SoftDeletableEntity
{
    private MeterTariffPeriod() { }

    public MeterTariffPeriod(Guid meterId, decimal ratePerUnit, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo = null)
    {
        if (meterId == Guid.Empty) throw new ArgumentException("A meter is required.", nameof(meterId));
        if (ratePerUnit < 0) throw new ArgumentOutOfRangeException(nameof(ratePerUnit));
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom) throw new ArgumentException("The end must be after the start.", nameof(effectiveTo));
        MeterId = meterId; RatePerUnit = ratePerUnit; EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
    }
    public Guid MeterId { get; private set; }
    public decimal RatePerUnit { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    public void CloseAt(DateTimeOffset effectiveTo)
    {
        if (effectiveTo <= EffectiveFrom) throw new ArgumentOutOfRangeException(nameof(effectiveTo));
        if (EffectiveTo.HasValue && EffectiveTo.Value != effectiveTo) throw new InvalidOperationException("The tariff period is already closed.");
        EffectiveTo = effectiveTo;
        Touch();
    }

    public void SetEffectiveTo(DateTimeOffset? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value <= EffectiveFrom)
            throw new ArgumentOutOfRangeException(nameof(effectiveTo));
        EffectiveTo = effectiveTo;
        Touch();
    }
}
