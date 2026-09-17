namespace NovaWallet.Domain.Common;

public static class DateTimeExtensions
{
    // West Africa Time is a fixed UTC+1 offset with no daylight saving.
    private static readonly TimeSpan WatOffset = TimeSpan.FromHours(1);

    public static DateOnly ToWatDate(this DateTimeOffset utcInstant)
        => DateOnly.FromDateTime(utcInstant.ToOffset(WatOffset).DateTime);
}
