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

    // Product names (L202) keep more: the brands of the sample models, their product lines and colour names, places
    private static readonly HashSet<string> ProductKeep = new(StringComparer.Ordinal)
    {
        // brands
        "Essence", "Calvin", "Klein", "Chanel", "Dior", "Dolce", "Gabbana", "Gucci", "Attitude", "Olay", "Vaseline", "Nescafé", "Whiskas",
        "Apple", "Asus", "Huawei", "Lenovo", "Dell", "Samsung", "Oppo", "Realme", "Vivo", "Amazon", "Beats", "Longines", "Rolex", "Nike",
        "Puma", "Heshe", "Prada", "Titleist", "Kawasaki", "Sony", "Canon", "Nikon", "Pentax", "Panasonic", "Transcend", "Microlife", "Nokia",
        "Xiaomi", "Brionvega", "Polk", "Fitbit", "Russell", "Hobbs", "Bosch", "Tatung", "Leica", "Gigabyte",
        // product lines and their colour names
        "Lash", "Princess", "One", "Coco", "Noir", "J'adore", "Shine", "Bloom", "Super", "Leaves", "Ultra", "Moisture", "Men", "Classic",
        "Pro", "Zenbook", "Duo", "Yoga", "Galaxy", "Tab", "Pen", "Plus", "Note", "Redmi", "Prism", "Black", "Sierra", "Starlight",
        "Space", "Grey", "Lightning", "Pack", "Battery", "Echo", "Max", "Flex", "Master", "Collection", "Automatic", "Cellini", "Date",
        "Moonphase", "Datejust", "Submariner", "Ingenieur", "Watch", "Series", "Aorus", "Air", "Jordan", "Future", "Rider", "Galleria",
        "Saffiano", "Alpha", "Coolpix", "Lumix", "Handycam", "Extra", "Bass", "Teddy", "Eau", "Parfum", "Toilette",
        // places
        "Hawaii", "Oxford", "Madras", "Menorca", "Úc", "Mỹ", "Ý", "Nauy",
    };

    // Proper names of more than one word whose words are common on their own ("sơn" is paint, "Lý Sơn" an island; "sơ mi" a shirt, "Xiaomi Mi" a phone; "Y" alone is a capital, "y tế" is health)
    private static readonly string[] ProductPhrases = ["Đà Lạt", "Lý Sơn", "Anh Quốc", "Hàn Quốc", "tiếng Anh", "Xiaomi Mi", "y tế"];

    /// <summary>True for a word a product name keeps capitalised inside the name.</summary>
    public static bool IsProperName(string word) =>
        Keep.Contains(word) || ProductKeep.Contains(word) || ProductPhrases.Any(p => p.Split(' ').Contains(word, StringComparer.Ordinal));

    /// <summary>
    /// A sample model's name in sentence case ("Nước Hoa Dior J'adore Eau De Parfum" → "Nước hoa Dior J'adore Eau de Parfum"):
    /// brands, product lines, places, acronyms and model numbers keep their capitals.
    /// </summary>
    public static string ProductName(string name)
    {
        var words = name.Split(' ');
        for (var i = 1; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length == 0 || Keep.Contains(w) || ProductKeep.Contains(w)) continue;
            // An acronym, a model number or a single Latin capital ("iPhone", "DSLR", "S10", "iPhone X", "chip M") stays as
            // written; "Ô" (ô tô), "Ở" are Vietnamese words
            if (w.Length == 1 && w[0] is >= 'A' and <= 'Z' || w.Skip(1).Any(c => char.IsUpper(c) || char.IsDigit(c))) continue;
            words[i] = w.ToLower(Vi);
        }
        var result = string.Join(' ', words);
        foreach (var phrase in ProductPhrases)
            result = System.Text.RegularExpressions.Regex.Replace(result, $@"(?<!\p{{L}}){phrase}(?!\p{{L}})", phrase,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return result;
    }

    /// <summary>A listing's name: the model's, then the wording a shop adds ("- Mẫu mới"), each in sentence case.</summary>
    public static string ListingName(string model, string? wording) =>
        wording is null ? ProductName(model) : $"{ProductName(model)} - {ProductName(wording)}";
}
