namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class ReadingAreaCandidateSelectorTests
{
    [Fact]
    public void SelectLargest_ChoosesLargestNumericTextBoxAndAddsPadding()
    {
        var selected = ReadingAreaCandidateSelector.SelectLargest(
        [
            new NumericTextLineCandidate("SERIAL 12345678", 50, 40, 420, 30),
            new NumericTextLineCandidate("00000528", 100, 200, 600, 90),
            new NumericTextLineCandidate("12", 20, 300, 700, 100)
        ], 1000, 500);

        Assert.NotNull(selected);
        Assert.True(selected.X < 0.1);
        Assert.InRange(selected.Y, 0.3, 0.32);
        Assert.True(selected.Width > 0.6);
        Assert.True(selected.Height > 0.18);
    }

    [Fact]
    public void SelectLargest_ClampsExpandedFrameToImageBounds()
    {
        var selected = ReadingAreaCandidateSelector.SelectLargest(
            [new NumericTextLineCandidate("123456", 0, 0, 100, 50)], 100, 100);

        Assert.NotNull(selected);
        Assert.Equal(0, selected.X);
        Assert.Equal(0, selected.Y);
        Assert.InRange(selected.X + selected.Width, 0, 1);
        Assert.InRange(selected.Y + selected.Height, 0, 1);
    }

    [Fact]
    public void SelectLargest_ReturnsNullWhenNoNumericLineHasAtLeastThreeDigits()
    {
        var selected = ReadingAreaCandidateSelector.SelectLargest(
        [
            new NumericTextLineCandidate("Meter 123456", 20, 10, 800, 80),
            new NumericTextLineCandidate("12", 10, 20, 500, 50),
            new NumericTextLineCandidate("---", 10, 30, 500, 50)
        ], 1000, 500);

        Assert.Null(selected);
    }
}
