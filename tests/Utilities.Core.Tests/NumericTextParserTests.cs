namespace Utilities.Core.Tests;

using System.Globalization;
using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class NumericTextParserTests
{
    [Theory]
    [InlineData("en-US", "1.5", true, 1.5)]
    [InlineData("en-US", "1,5", false, 0)]
    [InlineData("uk-UA", "1,5", true, 1.5)]
    [InlineData("uk-UA", "1.5", true, 1.5)]
    [InlineData("en-US", "1,500", false, 0)]
    public void TryDecimal_UsesDecimalSeparatorsWithoutGrouping(string cultureName, string text, bool expectedSuccess, decimal expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var success = NumericTextParser.TryDecimal(text, out var value);
            Assert.Equal(expectedSuccess, success);
            if (success) Assert.Equal(expected, value);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }
}
