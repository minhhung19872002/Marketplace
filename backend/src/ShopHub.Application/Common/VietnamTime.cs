namespace ShopHub.Application.Common;

/// <summary>Business dates (delivery estimates, expiry text) are in Vietnam time; storage stays UTC.</summary>
public static class VietnamTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static DateOnly Today(DateTimeOffset utcNow) => DateOnly.FromDateTime(ToLocal(utcNow).DateTime);

    /// <summary>"14:05 07/10/2026" — the same shape as the frontends' formatDateTime.</summary>
    public static string Format(DateTimeOffset utc) => ToLocal(utc).ToString("HH:mm dd/MM/yyyy");
}
