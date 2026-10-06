namespace ShopHub.Application.Common;

/// <summary>Business dates (delivery estimates, expiry text) are in Vietnam time; storage stays UTC.</summary>
public static class VietnamTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static DateOnly Today(DateTimeOffset utcNow) => DateOnly.FromDateTime(ToLocal(utcNow).DateTime);

    /// <summary>"14:05 07/10/2026" — the same shape as the frontends' formatDateTime.</summary>
    public static string Format(DateTimeOffset utc) => ToLocal(utc).ToString("HH:mm dd/MM/yyyy");

    /// <summary>Add delivery days, skipping Sundays and the given holidays.</summary>
    public static DateOnly AddWorkingDays(DateOnly from, int days, IReadOnlySet<DateOnly> holidays)
    {
        var date = from;
        var added = 0;
        while (added < days)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek == DayOfWeek.Sunday || holidays.Contains(date)) continue;
            added++;
        }
        return date;
    }
}
