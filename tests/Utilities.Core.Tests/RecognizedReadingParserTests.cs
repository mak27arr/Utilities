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
}
