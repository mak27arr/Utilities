namespace Utilities.Core.BusinessLogic;

public static class ReadingTimestamp
{
    public static DateTimeOffset ToLocalTime(DateTimeOffset timestamp) => timestamp.ToLocalTime();
}
