namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;
using Xunit;

public sealed class MeterHistoryCsvFormatterTests
{
    [Fact]
    public void FormatReadings_UsesInvariantValuesAndQuotesLocalizedHeaders()
    {
        var meterId = Guid.NewGuid();
        var older = new Reading(meterId, 10.5, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), 2m, null, null);
        var newer = new Reading(meterId, 12.75, new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), 2m, 2.25, 4.5m);

        var csv = MeterHistoryCsvFormatter.FormatReadings([newer, older], ["Дата;час", "Показник", "Спожито", "Сума"]);

        Assert.Contains("\"Дата;час\"", csv);
        Assert.Contains("\"12.75\";\"2.25\";\"4.5\"", csv);
        Assert.True(csv.IndexOf("10.5", StringComparison.Ordinal) < csv.IndexOf("12.75", StringComparison.Ordinal));
    }
}
