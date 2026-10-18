namespace Utilities.Core.BusinessLogic;

using System.Globalization;
using Utilities.Core.Domain.Models;

public static class MeterHistoryCsvFormatter
{
    public static string FormatReadings(IEnumerable<Reading> readings, IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ValidateHeaders(headers);
        var lines = new List<string> { Row(headers) };
        lines.AddRange(readings.OrderBy(item => item.MeasuredAt).Select(reading => Row([
            reading.MeasuredAt.ToString("O", CultureInfo.InvariantCulture),
            reading.ConfirmedValue.ToString("R", CultureInfo.InvariantCulture),
            reading.ConsumptionSnapshot?.ToString("R", CultureInfo.InvariantCulture),
            reading.AmountSnapshot?.ToString(CultureInfo.InvariantCulture)])));
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatTariffs(IEnumerable<MeterTariffPeriod> tariffs, string unit, IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(tariffs);
        ValidateHeaders(headers);
        var lines = new List<string> { Row(headers) };
        lines.AddRange(tariffs.OrderBy(item => item.EffectiveFrom).Select(tariff => Row([
            tariff.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            tariff.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture),
            tariff.RatePerUnit.ToString(CultureInfo.InvariantCulture), unit])));
        return string.Join(Environment.NewLine, lines);
    }

    private static void ValidateHeaders(IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Count != 4) throw new ArgumentException("Exactly four CSV column headers are required.", nameof(headers));
    }

    private static string Row(IEnumerable<string?> fields) => string.Join(';', fields.Select(Escape));
    private static string Escape(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
