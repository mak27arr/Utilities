namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class ReadingCsvParserTests
{
    [Fact]
    public void Parse_AcceptsHeaderSemicolonAndCommaDecimals_AndSortsByDate()
    {
        var rows = ReadingCsvParser.Parse("Date;Reading\n02/03/2025;12,75\n01/03/2025;10,5");

        Assert.Collection(rows,
            first => { Assert.Equal(new DateTime(2025, 3, 1), first.Date); Assert.Equal(10.5, first.Value); },
            second => { Assert.Equal(new DateTime(2025, 3, 2), second.Date); Assert.Equal(12.75, second.Value); });
    }

    [Theory]
    [InlineData("31/02/2025,12", ReadingCsvErrorCode.InvalidDate)]
    [InlineData("01/03/2025,-1", ReadingCsvErrorCode.InvalidValue)]
    [InlineData("01-03-2025,12", ReadingCsvErrorCode.InvalidDate)]
    public void Parse_RejectsInvalidDateOrReading(string csv, ReadingCsvErrorCode errorCode)
    {
        var exception = Assert.Throws<ReadingCsvRowException>(() => ReadingCsvParser.Parse(csv));
        Assert.Equal(1, exception.LineNumber);
        Assert.Equal(errorCode, exception.ErrorCode);
    }

    [Fact]
    public void Parse_RejectsDuplicateDates()
    {
        var exception = Assert.Throws<ReadingCsvRowException>(() => ReadingCsvParser.Parse("01/03/2025,12\n01/03/2025,13"));
        Assert.Equal(2, exception.LineNumber);
        Assert.Equal(ReadingCsvErrorCode.DuplicateDate, exception.ErrorCode);
    }

    [Fact]
    public void Parse_DoesNotTreatMalformedFirstDataRowAsAHeader()
    {
        var exception = Assert.Throws<ReadingCsvRowException>(() =>
            ReadingCsvParser.Parse("not-a-date;100\n01/03/2025;110"));

        Assert.Equal(1, exception.LineNumber);
        Assert.Equal(ReadingCsvErrorCode.InvalidDate, exception.ErrorCode);
    }

    [Fact]
    public void Parse_ReportsPhysicalLineNumberWhenCsvContainsBlankLines()
    {
        var exception = Assert.Throws<ReadingCsvRowException>(() =>
            ReadingCsvParser.Parse("\nDate,Reading\n\n01/03/2025,12\n01/03/2025,13"));

        Assert.Equal(5, exception.LineNumber);
        Assert.Equal(ReadingCsvErrorCode.DuplicateDate, exception.ErrorCode);
    }
}
