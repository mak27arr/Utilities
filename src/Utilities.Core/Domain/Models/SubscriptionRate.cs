namespace Utilities.Core.Domain.Models;

public sealed class SubscriptionRate : SoftDeletableEntity
{
    private SubscriptionRate() { }

    public SubscriptionRate(Guid subscriptionId, decimal monthlyFee, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo = null)
    {
        if (subscriptionId == Guid.Empty) throw new ArgumentException("A subscription is required.", nameof(subscriptionId));
        if (monthlyFee < 0) throw new ArgumentOutOfRangeException(nameof(monthlyFee));
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom) throw new ArgumentException("The end must be after the start.", nameof(effectiveTo));
        SubscriptionId = subscriptionId; MonthlyFee = monthlyFee; EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
    }
    public Guid SubscriptionId { get; private set; }
    public decimal MonthlyFee { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    public void CloseAt(DateTimeOffset effectiveTo)
    {
        if (effectiveTo <= EffectiveFrom) throw new ArgumentOutOfRangeException(nameof(effectiveTo));
        if (EffectiveTo.HasValue && EffectiveTo.Value != effectiveTo) throw new InvalidOperationException("The rate period is already closed.");
        EffectiveTo = effectiveTo;
        Touch();
    }
}
