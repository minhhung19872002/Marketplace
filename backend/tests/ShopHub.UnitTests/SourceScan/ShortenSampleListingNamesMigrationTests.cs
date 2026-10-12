using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using ShopHub.Application.Common;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.SourceScan;

/// <summary>
/// L209: migration ShortenSampleListingNames takes the shop wording off sample listings ("… - Chính hãng"). Its lists must be
/// what the seeder writes: every model name, every shop wording that is not a second-hand condition, each with its slug —
/// a missed one keeps the wording on old databases only, which no other test would see.
/// </summary>
public class ShortenSampleListingNamesMigrationTests
{
    private static readonly string Migration = File.ReadAllText(Directory.GetFiles(
        Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Persistence", "Migrations"), "*_ShortenSampleListingNames.cs").Single());

    private static string Values(string table)
    {
        var block = Regex.Match(Migration, $@"INSERT INTO l209_{table} VALUES(?<body>.*?'\));", RegexOptions.Singleline);
        block.Success.Should().BeTrue($"migration có danh sách {table}");
        return block.Groups["body"].Value;
    }

    private static readonly Regex Text = new(@"'((?:[^']|'')*)'");

    private static IEnumerable<string> Wordings()
    {
        var source = File.ReadAllText(Path.Combine(RepoFiles.BackendRoot, "src", "ShopHub.Infrastructure", "Seed", "ProductGenerator.cs"));
        var block = Regex.Match(source, @"Wordings = new\(\)\s*\{(?<body>.*?)\n\s*\};", RegexOptions.Singleline).Groups["body"].Value;
        return Regex.Matches(block, @"\] = \[(?<l>[^\]]*)\]").SelectMany(x => Regex.Matches(x.Groups["l"].Value, "\"([^\"]+)\"").Select(y => y.Groups[1].Value));
    }

    [Fact]
    public void The_model_list_is_every_sample_model_as_the_seeder_names_it()
    {
        var listed = Text.Matches(Values("m")).Select(m => m.Groups[1].Value.Replace("''", "'")).ToHashSet();
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Seed", "Data", "product-models.json")));
        var names = doc.RootElement.EnumerateArray().Select(m => SeedText.ProductName(m.GetProperty("name").GetString()!)).ToHashSet();
        listed.Should().BeEquivalentTo(names);
    }

    [Fact]
    public void The_wording_list_is_every_new_item_wording_with_its_slug()
    {
        var pairs = Regex.Matches(Values("w"), @"\('((?:[^']|'')*)', '((?:[^']|'')*)'\)")
            .Select(m => (Wording: m.Groups[1].Value, Slug: m.Groups[2].Value)).ToList();
        var wordings = Wordings().ToList();
        wordings.Should().NotBeEmpty();
        // Second-hand wordings ("*Đã Qua Sử Dụng 99%") say the condition: they stay
        pairs.Select(p => p.Wording).Should().BeEquivalentTo(wordings.Where(w => !w.StartsWith('*')).Select(SeedText.ProductName).Distinct());
        pairs.Where(p => Slug.From(p.Wording) != p.Slug).Should().BeEmpty();
    }

    [Fact]
    public void About_one_listing_in_five_takes_a_shop_wording()
    {
        // L209: seven in ten did. A model's first listing always takes its own name; ~1,000 listings of 309 models
        var rng = new Random(2026);
        var plan = Enumerable.Range(0, 1000).Select(i => i < 309 ? 0 : 1 + i % 7).ToList();
        var worded = plan.Count(copy => !ProductGenerator.PlainName(copy, 0, rng));
        ProductGenerator.PlainName(0, 0, new Random(1)).Should().BeTrue();
        ProductGenerator.PlainName(3, 1, new Random(1)).Should().BeFalse("lần thử lại (trùng tên trong shop) luôn thêm cụm của shop");
        (worded / 1000.0).Should().BeInRange(0.12, 0.25);
    }
}
