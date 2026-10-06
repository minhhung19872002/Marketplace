using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ShopHub.Infrastructure.Commerce.Providers;

/// <summary>Small helpers shared by the real gateways / carriers: signatures, Vietnam time, ASCII text.</summary>
public static class ProviderText
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    public static string HmacSha512Hex(string key, string data) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    public static string HmacSha256Hex(string key, string data) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    /// <summary>Constant-time comparison of two hex signatures, case-insensitive.</summary>
    public static bool SameHex(string? a, string? b) =>
        a is not null && b is not null && a.Length == b.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()), Encoding.ASCII.GetBytes(b.ToLowerInvariant()));

    public static bool SameSecret(string? a, string? b) =>
        a is not null && b is not null
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    /// <summary>yyyyMMddHHmmss in Vietnam time (VNPay dates).</summary>
    public static string VnStamp(DateTimeOffset at) => at.ToOffset(Vietnam).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseVnStamp(string? s) =>
        DateTime.TryParseExact(s, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? new DateTimeOffset(d, Vietnam).ToUniversalTime()
            : null;

    /// <summary>Vietnamese without tones (gateways ask for plain ASCII order descriptions).</summary>
    public static string Ascii(string text)
    {
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && ch < 128) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>VNPay's canonical "key=value&amp;…" string: keys in ordinal order, both sides URL-encoded (space as '+').</summary>
    public static string VnPayCanonical(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields.Where(f => !string.IsNullOrEmpty(f.Value)).OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{WebUtility.UrlEncode(f.Key)}={WebUtility.UrlEncode(f.Value)}"));
}
