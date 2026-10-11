using System.Globalization;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// The sample catalogue keeps its category names in Title Case as keys (catalog-seed.json, product-models.json, the art
/// tools); what is written to the database — and shown — follows the capitalisation policy (docs/00 #209): sentence case
/// ("Điện Thoại &amp; Phụ Kiện" → "Điện thoại &amp; phụ kiện"), proper names and acronyms kept ("iPad", "DSLR", "Bluetooth").
/// </summary>
public static class SeedText
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    // Names that stay capitalised inside a phrase (brands, a standard); words with an inner capital (iPad, SpO2) or in
    // capitals (DSLR) are kept by the rule itself
    private static readonly HashSet<string> Keep = new(StringComparer.Ordinal) { "Bluetooth", "Lego", "Omega-3", "Mall" };

    public static string SentenceCase(string name)
    {
        var words = name.Split(' ');
        for (var i = 1; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length == 0 || Keep.Contains(w) || w.Skip(1).Any(char.IsUpper)) continue;
            words[i] = w.ToLower(Vi);
        }
        return string.Join(' ', words);
    }
}
