namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class ReadingTimestampTests
{
    [Fact]
    public void ToLocalTime_ConvertsReloadedUtcTimestampToDeviceLocalTime()
    {
        var storedUtc = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

        var displayed = ReadingTimestamp.ToLocalTime(storedUtc);

        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(storedUtc), displayed.Offset);
        Assert.Equal(TimeZoneInfo.ConvertTime(storedUtc, TimeZoneInfo.Local).DateTime, displayed.DateTime);
    }
}
