namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.ValueObjects;

/// <summary>A text line box reported by an OCR engine in source-image pixel coordinates.</summary>
public readonly record struct NumericTextLineCandidate(string Text, double X, double Y, double Width, double Height);

/// <summary>Selects the most prominent numeric OCR line as a suggested meter-reading crop.</summary>
public static class ReadingAreaCandidateSelector
{
    private const int MinimumDigits = 3;
    private const double HorizontalPaddingInLineHeights = 0.12;
    private const double VerticalPaddingInLineHeights = 0.5;

    public static NormalizedReadingArea? SelectLargest(IEnumerable<NumericTextLineCandidate> candidates,
        int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));

        var candidate = candidates
            .Where(IsUsableNumericLine)
            .OrderByDescending(item => item.Width * item.Height)
            .ThenByDescending(item => item.Height)
            .ThenByDescending(item => item.Width)
            .FirstOrDefault();
        if (!IsUsableNumericLine(candidate)) return null;

        var horizontalPadding = candidate.Height * HorizontalPaddingInLineHeights;
        var verticalPadding = candidate.Height * VerticalPaddingInLineHeights;
        var left = Math.Clamp(candidate.X - horizontalPadding, 0, imageWidth - 1d);
        var top = Math.Clamp(candidate.Y - verticalPadding, 0, imageHeight - 1d);
        var right = Math.Clamp(candidate.X + candidate.Width + horizontalPadding, left + 1, imageWidth);
        var bottom = Math.Clamp(candidate.Y + candidate.Height + verticalPadding, top + 1, imageHeight);

        return new NormalizedReadingArea(left / imageWidth, top / imageHeight,
            (right - left) / imageWidth, (bottom - top) / imageHeight);
    }

    private static bool IsUsableNumericLine(NumericTextLineCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text) ||
            !double.IsFinite(candidate.X) || !double.IsFinite(candidate.Y) ||
            !double.IsFinite(candidate.Width) || !double.IsFinite(candidate.Height) ||
            candidate.X < 0 || candidate.Y < 0 || candidate.Width <= 0 || candidate.Height <= 0)
            return false;

        var digits = 0;
        foreach (var character in candidate.Text)
        {
            if (char.IsAsciiDigit(character))
            {
                digits++;
                continue;
            }

            if (!char.IsWhiteSpace(character) && character is not ('.' or ',' or ':' or '\'' or '’' or '-' or '_'))
                return false;
        }

        return digits >= MinimumDigits;
    }
}
