namespace Utilities.Core.BusinessLogic;

using System.Globalization;

/// <summary>Parses an OCR string only when it contains a single unambiguous numeric token.</summary>
public static class RecognizedReadingParser
{
    /// <summary>Chooses the largest OCR text row that parses as a numeric reading, ignoring rows with letters.</summary>
    public static bool TrySelectLargestNumericLine(IEnumerable<NumericTextLineCandidate> lines,
        out string selectedText, out double value)
    {
        ArgumentNullException.ThrowIfNull(lines);
        selectedText = string.Empty;
        value = default;

        foreach (var line in lines
                     .Where(line => double.IsFinite(line.Width) && double.IsFinite(line.Height) && line.Width > 0 && line.Height > 0)
                     .OrderByDescending(line => line.Width * line.Height)
                     .ThenByDescending(line => line.Height)
                     .ThenByDescending(line => line.Width))
        {
            if (!TryParse(line.Text, out value)) continue;
            selectedText = line.Text.Trim();
            return true;
        }

        value = default;
        return false;
    }

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
