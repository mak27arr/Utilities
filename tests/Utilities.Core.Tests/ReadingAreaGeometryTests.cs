namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.ValueObjects;
using Xunit;

public sealed class ReadingAreaGeometryTests
{
    [Theory]
    [InlineData(0d, 0d, 1d, 1d)]
    [InlineData(0.25d, 0.35d, 0.5d, 0.3d)]
    [InlineData(0.9d, 0.8d, 0.1d, 0.2d)]
    public void DisplayOverlayAndCropUseTheSamePixelEdges(double x, double y, double width, double height)
    {
        var area = new NormalizedReadingArea(x, y, width, height);
        const int imageWidth = 1000;
        const int imageHeight = 400;
        const double displayWidth = 500;
        const double displayHeight = 200;

        var pixels = ReadingAreaGeometry.GetPixelBounds(area, imageWidth, imageHeight);
        var display = ReadingAreaGeometry.GetDisplayBounds(area, imageWidth, imageHeight, displayWidth, displayHeight);

        Assert.Equal(pixels.Left * displayWidth / imageWidth, display.X);
        Assert.Equal(pixels.Top * displayHeight / imageHeight, display.Y);
        Assert.Equal(pixels.Width * displayWidth / imageWidth, display.Width);
        Assert.Equal(pixels.Height * displayHeight / imageHeight, display.Height);
        Assert.InRange(pixels.Left, 0, imageWidth - 1);
        Assert.InRange(pixels.Top, 0, imageHeight - 1);
        Assert.InRange(pixels.Left + pixels.Width, 1, imageWidth);
        Assert.InRange(pixels.Top + pixels.Height, 1, imageHeight);
    }
}
