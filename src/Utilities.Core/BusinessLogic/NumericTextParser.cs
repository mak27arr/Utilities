namespace Utilities.Core.BusinessLogic;

using System.Globalization;

/// <summary>Parses user-entered numbers without interpreting decimal separators as thousands separators.</summary>
public static class NumericTextParser
{
    private const NumberStyles DecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    public static bool TryDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static bool TryDecimal(string? text, out decimal value) =>
        decimal.TryParse(text, DecimalStyles, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, DecimalStyles, CultureInfo.InvariantCulture, out value);
}
