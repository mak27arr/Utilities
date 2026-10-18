namespace Utilities.Core.BusinessLogic;

using System.Globalization;

/// <summary>Parses an OCR string only when it contains a single unambiguous numeric token.</summary>
public static class RecognizedReadingParser
{
    public static bool TryParse(string? recognizedText, out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(recognizedText)) return false;
        var compact = string.Concat(recognizedText.Where(character => !char.IsWhiteSpace(character)));
        if (compact.Length == 0 || compact.Any(character => !char.IsAsciiDigit(character) && character is not '.' and not ',')) return false;
        if (compact.Count(character => character is '.' or ',') > 1) return false;
        var normalized = compact.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value)
            && value >= 0;
    }
}
