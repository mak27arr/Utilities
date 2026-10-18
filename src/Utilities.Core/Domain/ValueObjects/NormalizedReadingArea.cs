namespace Utilities.Core.Domain.ValueObjects;

public sealed class NormalizedReadingArea
{
    private NormalizedReadingArea() { }

    public NormalizedReadingArea(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(x), "Crop coordinates must be finite.");
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(width), "The crop rectangle must fit inside normalized image bounds.");
        X = x; Y = y; Width = width; Height = height;
    }

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }
}
