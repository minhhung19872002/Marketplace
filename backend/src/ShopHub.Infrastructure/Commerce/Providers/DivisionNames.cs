using System.Text;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Commerce.Providers;

public record DivisionNames(string Province, string District, string Ward);

/// <summary>
/// Names of a route point from ShopHub's administrative divisions (Tổng cục Thống kê codes). Carriers have their own
/// ids (GHN) or take names (GHTK), so matching goes through a normalised name: no tones, no "Tỉnh / Quận / Phường…".
/// </summary>
public sealed class DivisionNameResolver(ShopHubDbContext db)
{
    private static readonly string[] Prefixes =
        ["thanh pho ", "tinh ", "tp. ", "tp ", "quan ", "huyen ", "thi xa ", "thi tran ", "phuong ", "xa ", "q. ", "h. ", "p. "];

    public async Task<DivisionNames?> NamesAsync(RoutePoint p, CancellationToken ct)
    {
        var codes = new[] { p.ProvinceCode, p.DistrictCode, p.WardCode };
        var names = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
        return names.TryGetValue(p.ProvinceCode, out var province) && names.TryGetValue(p.DistrictCode, out var district)
            ? new DivisionNames(province, district, names.GetValueOrDefault(p.WardCode) ?? "")
            : null;
    }

    /// <summary>"Thành phố Hồ Chí Minh" → "hochiminh", "Quận 1" → "1", "Phường Bến Nghé" → "bennghe".</summary>
    public static string Normalise(string name)
    {
        var s = ProviderText.Ascii(name).ToLowerInvariant().Trim();
        foreach (var prefix in Prefixes)
            if (s.StartsWith(prefix, StringComparison.Ordinal))
            {
                s = s[prefix.Length..];
                break;
            }
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        // "Quận 01" and "Quận 1" are the same district
        return sb.ToString().TrimStart('0') is { Length: > 0 } trimmed && char.IsDigit(sb[0]) ? trimmed : sb.ToString();
    }

    public static bool Matches(string ours, string name, IEnumerable<string>? extensions)
    {
        var key = Normalise(ours);
        return Normalise(name) == key || (extensions ?? []).Any(e => Normalise(e) == key);
    }
}
