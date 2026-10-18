namespace Utilities.Mobile;

using Utilities.Core.BusinessLogic;

internal static class NumericInputParser
{
    public static bool TryDouble(string? text, out double value) => NumericTextParser.TryDouble(text, out value);

    public static bool TryDecimal(string? text, out decimal value) => NumericTextParser.TryDecimal(text, out value);
}




