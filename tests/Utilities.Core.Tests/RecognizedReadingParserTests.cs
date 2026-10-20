namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class RecognizedReadingParserTests
{
    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData(" 123,45\n", 123.45)]
    [InlineData("0007", 7)]
    public void TryParse_ParsesUnambiguousNonNegativeMeterValue(string text, double expected)
    {
        Assert.True(RecognizedReadingParser.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1O.2")]
    [InlineData("1.2,3")]
    [InlineData("-2")]
    [InlineData("1e3")]
    public void TryParse_RejectsAmbiguousOrNonNumericText(string text)
    {
        Assert.False(RecognizedReadingParser.TryParse(text, out _));
    }

    [Fact]
    public void TrySelectLargestNumericLine_SkipsRowsContainingLettersAndUsesLargestValidNumber()
    {
        var selected = RecognizedReadingParser.TrySelectLargestNumericLine(
        [
            new NumericTextLineCandidate("METER 999999", 0, 0, 900, 100),
            new NumericTextLineCandidate("12345678", 0, 120, 500, 60),
            new NumericTextLineCandidate("0000528", 0, 220, 700, 80)
        ], out var text, out var value);

        Assert.True(selected);
        Assert.Equal("0000528", text);
        Assert.Equal(528d, value);
    }

    [Fact]
    public void TrySelectLargestNumericLine_ReturnsFalseWhenEveryRowContainsLetters()
    {
        var selected = RecognizedReadingParser.TrySelectLargestNumericLine(
        [
            new NumericTextLineCandidate("METER 12345", 0, 0, 500, 50),
            new NumericTextLineCandidate("G4", 0, 60, 300, 40)
        ], out var text, out var value);

        Assert.False(selected);
        Assert.Empty(text);
        Assert.Equal(0d, value);
    }
}
