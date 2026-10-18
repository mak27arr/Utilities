namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Xunit;

public sealed class TariffRulesTests
{
    [Fact]
    public void SelectMeterTariff_UsesPeriodEffectiveAtReadingTime()
    {
        var meterId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = start.AddMonths(1);
        var old = new MeterTariffPeriod(meterId, 1.5m, start, next);
        var current = new MeterTariffPeriod(meterId, 2.25m, next);

        Assert.Same(old, TariffRules.SelectMeterTariff(next.AddTicks(-1), [old, current]));
        Assert.Same(current, TariffRules.SelectMeterTariff(next, [old, current]));
    }

    [Fact]
    public void SelectMeterTariff_IgnoresSoftDeletedPeriod()
    {
        var period = new MeterTariffPeriod(Guid.NewGuid(), 1m, DateTimeOffset.UtcNow);
        period.MarkDeleted();

        Assert.Null(TariffRules.SelectMeterTariff(DateTimeOffset.UtcNow, [period]));
    }

    [Fact]
    public void SelectSubscriptionRate_UsesRateEffectiveAtBillingTime()
    {
        var subscriptionId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var old = new SubscriptionRate(subscriptionId, 15m, start, start.AddDays(10));
        var current = new SubscriptionRate(subscriptionId, 18m, start.AddDays(10));

        Assert.Same(current, TariffRules.SelectSubscriptionRate(start.AddDays(11), [old, current]));
    }
}
