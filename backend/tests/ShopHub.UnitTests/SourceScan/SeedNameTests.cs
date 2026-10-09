using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace ShopHub.UnitTests.SourceScan;

/// <summary>
/// G3 B1 (L159): sample product names read like a machine numbered them ("Chanh Vàng Mỹ Mọng Nước – Loại 1"). Neither the
/// model names nor the wordings the generator appends may end in a number-like suffix.
/// </summary>
public class SeedNameTests
{
    private static readonly Regex NumberedSuffix = new(@"(\b(Loại|Loai|Số|So|No\.?|Mẫu|Kiểu)\s*\d+|#\s*\d+|\s[-–]\s*\d+)\s*$", RegexOptions.IgnoreCase);

    private static string SeedFile(params string[] parts) =>
        Path.Combine([RepoFiles.BackendRoot, "src", "ShopHub.Infrastructure", "Seed", .. parts]);

    [Fact]
    public void Sample_model_names_have_no_numbered_suffix()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(SeedFile("Data", "product-models.json")));
        var offenders = json.RootElement.EnumerateArray().Select(m => m.GetProperty("name").GetString()!)
            .Where(n => NumberedSuffix.IsMatch(n)).ToList();
        offenders.Should().BeEmpty("tên mẫu không mang hậu tố đánh số");
    }

    [Fact]
    public void Wordings_appended_by_the_generator_have_no_numbered_suffix()
    {
        var source = File.ReadAllText(SeedFile("ProductGenerator.cs"));
        var block = Regex.Match(source, @"Wordings = new\(\)\s*\{(?<body>.*?)\n\s*\};", RegexOptions.Singleline);
        block.Success.Should().BeTrue("ProductGenerator còn bảng Wordings");
        var wordings = Regex.Matches(block.Groups["body"].Value, @"""(?<w>[^""]+)""").Select(m => m.Groups["w"].Value.TrimStart('*'))
            .Where(w => !w.Contains('&')).ToList();
        wordings.Should().NotBeEmpty();
        wordings.Where(w => NumberedSuffix.IsMatch($"Tên - {w}")).Should().BeEmpty("phần thêm vào tên không được là \"Loại 1\", \"- 2\"…");
    }
}
