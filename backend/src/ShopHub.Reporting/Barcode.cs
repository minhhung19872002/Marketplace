using System.Globalization;
using System.Text;

namespace ShopHub.Reporting;

/// <summary>
/// Code 39 barcode as SVG (tracking numbers and order codes are upper-case letters and digits). Each symbol is 9
/// elements — bar, space, bar… — of which exactly 3 are wide; symbols are separated by one narrow space and the
/// data is framed by the '*' start/stop symbol.
/// </summary>
public static class Code39
{
    // n = narrow, w = wide (ratio 1 : 2.5)
    public static readonly IReadOnlyDictionary<char, string> Patterns = new Dictionary<char, string>
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn", ['4'] = "nnnwwnnnw",
        ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw", ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw", ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw", ['E'] = "wnnnwwnnn",
        ['F'] = "nnwnwwnnn", ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn", ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww", ['O'] = "wnnnwnnwn",
        ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn", ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw", ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw", ['Y'] = "wwnnwnnnn",
        ['Z'] = "nwwnwnnnn", ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['*'] = "nwnnwnwnn",
    };

    /// <summary>Bar widths in modules (bars at even indexes), quiet zones excluded.</summary>
    public static IReadOnlyList<double> Modules(string data)
    {
        var text = data.ToUpperInvariant();
        if (text.Any(c => c == '*' || !Patterns.ContainsKey(c))) throw new ArgumentException($"Không mã hoá được \"{data}\" bằng Code 39.", nameof(data));
        var widths = new List<double>();
        foreach (var c in $"*{text}*")
        {
            if (widths.Count > 0) widths.Add(1);
            widths.AddRange(Patterns[c].Select(e => e == 'w' ? 2.5 : 1.0));
        }
        return widths;
    }

    public static string Svg(string data, double height = 40)
    {
        var modules = Modules(data);
        var quiet = 10.0;
        var total = modules.Sum() + quiet * 2;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total:0.##} {height:0.##}\" preserveAspectRatio=\"none\">");
        var x = quiet;
        for (var i = 0; i < modules.Count; i++)
        {
            if (i % 2 == 0) sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x:0.##}\" y=\"0\" width=\"{modules[i]:0.##}\" height=\"{height:0.##}\" fill=\"black\"/>");
            x += modules[i];
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
