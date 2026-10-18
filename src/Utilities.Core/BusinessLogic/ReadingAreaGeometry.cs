namespace Utilities.Core.BusinessLogic;

using Utilities.Core.Domain.ValueObjects;

public readonly record struct ReadingAreaRectangle(double X, double Y, double Width, double Height);
public readonly record struct ReadingAreaPixelBounds(int Left, int Top, int Width, int Height);

/// <summary>Maps the normalized crop saved by the app to both the preview overlay and source bitmap.</summary>
public static class ReadingAreaGeometry
{
    public static ReadingAreaPixelBounds GetPixelBounds(NormalizedReadingArea area, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));

        var left = Math.Clamp((int)Math.Floor(area.X * imageWidth), 0, imageWidth - 1);
        var top = Math.Clamp((int)Math.Floor(area.Y * imageHeight), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling((area.X + area.Width) * imageWidth), left + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling((area.Y + area.Height) * imageHeight), top + 1, imageHeight);
        return new ReadingAreaPixelBounds(left, top, right - left, bottom - top);
    }

    public static ReadingAreaRectangle GetDisplayBounds(NormalizedReadingArea area, int imageWidth, int imageHeight,
        double displayWidth, double displayHeight)
    {
        if (!double.IsFinite(displayWidth) || displayWidth <= 0) throw new ArgumentOutOfRangeException(nameof(displayWidth));
        if (!double.IsFinite(displayHeight) || displayHeight <= 0) throw new ArgumentOutOfRangeException(nameof(displayHeight));

        var pixels = GetPixelBounds(area, imageWidth, imageHeight);
        return new ReadingAreaRectangle(
            pixels.Left * displayWidth / imageWidth,
            pixels.Top * displayHeight / imageHeight,
            pixels.Width * displayWidth / imageWidth,
            pixels.Height * displayHeight / imageHeight);
    }
}
