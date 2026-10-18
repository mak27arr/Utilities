namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.Models;

public static class TariffRules
{
    public static MeterTariffPeriod? SelectMeterTariff(DateTimeOffset measuredAt, IEnumerable<MeterTariffPeriod> periods) =>
        periods.Where(period => !period.IsDeleted && period.EffectiveFrom <= measuredAt && (!period.EffectiveTo.HasValue || measuredAt < period.EffectiveTo.Value))
            .OrderByDescending(period => period.EffectiveFrom)
            .FirstOrDefault();

    public static SubscriptionRate? SelectSubscriptionRate(DateTimeOffset billedAt, IEnumerable<SubscriptionRate> rates) =>
        rates.Where(rate => !rate.IsDeleted && rate.EffectiveFrom <= billedAt && (!rate.EffectiveTo.HasValue || billedAt < rate.EffectiveTo.Value))
            .OrderByDescending(rate => rate.EffectiveFrom)
            .FirstOrDefault();
}
