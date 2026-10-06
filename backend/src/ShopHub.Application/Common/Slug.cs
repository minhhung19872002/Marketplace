using System.Globalization;
using System.Text;

namespace ShopHub.Application.Common;

public static class Slug
{
    /// <summary>"Áo Thun Nam Cotton Cao Cấp" → "ao-thun-nam-cotton-cao-cap" (max 80 chars).</summary>
    public static string From(string text)
    {
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var dash = false;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 80 ? slug[..80].TrimEnd('-') : slug;
    }

    /// <summary>Accent-insensitive, lower-case form used for search and keyword checks.</summary>
    public static string Fold(string text) => From(text).Replace('-', ' ');
}
