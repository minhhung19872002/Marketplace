using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.SourceScan;

/// <summary>
/// L202: migration SentenceCaseProductNames renames the sample products, tiers and options earlier seeds wrote in Title Case.
/// Its pairs must be exactly what the seeder writes now and cover every model, shop wording, tier and option that changes —
/// a missed one stays Title Case on old databases only, which no other test would see.
/// </summary>
public class SentenceCaseProductNamesMigrationTests
{
    private static readonly string Migration = File.ReadAllText(Directory.GetFiles(
        Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Persistence", "Migrations"), "*_SentenceCaseProductNames.cs").Single());

    /// <summary>The (old, new) pairs of one VALUES list of the migration, by its temp table ("m" for l202_m, "w", "t", "o").</summary>
    private static List<(string Old, string New)> Pairs(string list)
    {
        var block = Regex.Match(Migration, $@"INSERT INTO l202_{list} VALUES(?<body>.*?'\));", RegexOptions.Singleline);
        block.Success.Should().BeTrue($"migration có danh sách {list}");
        return Regex.Matches(block.Groups["body"].Value, @"\('((?:[^']|'')*)', '((?:[^']|'')*)'\)")
            .Select(m => (m.Groups[1].Value.Replace("''", "'"), m.Groups[2].Value.Replace("''", "'"))).ToList();
    }

    private static List<JsonElement> Models()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Seed", "Data",
            "product-models.json")));
        return doc.RootElement.EnumerateArray().ToList();
    }

    [Fact]
    public void Every_model_name_is_renamed_to_what_the_seeder_writes()
    {
        var pairs = Pairs("m");
        pairs.Where(p => SeedText.ProductName(p.Old) != p.New).Should().BeEmpty();
        var renamed = pairs.Select(p => p.Old).ToHashSet();
        Models().Select(m => m.GetProperty("name").GetString()!).Where(n => SeedText.ProductName(n) != n && !renamed.Contains(n)).Should().BeEmpty();
    }

    [Fact]
    public void Every_shop_wording_is_renamed_to_what_the_seeder_writes()
    {
        var pairs = Pairs("w");
        pairs.Where(p => SeedText.ProductName(p.Old) != p.New).Should().BeEmpty();
        var source = File.ReadAllText(Path.Combine(RepoFiles.BackendRoot, "src", "ShopHub.Infrastructure", "Seed", "ProductGenerator.cs"));
        var block = Regex.Match(source, @"Wordings = new\(\)\s*\{(?<body>.*?)\n\s*\};", RegexOptions.Singleline).Groups["body"].Value;
        var wordings = Regex.Matches(block, @"\] = \[(?<l>[^\]]*)\]")
            .SelectMany(x => Regex.Matches(x.Groups["l"].Value, "\"([^\"]+)\"").Select(y => y.Groups[1].Value.TrimStart('*'))).ToHashSet();
        wordings.Should().NotBeEmpty();
        var renamed = pairs.Select(p => p.Old).ToHashSet();
        wordings.Where(w => SeedText.ProductName(w) != w && !renamed.Contains(w)).Should().BeEmpty();
    }

    [Fact]
    public void Every_tier_and_option_is_renamed_to_what_the_seeder_writes()
    {
        var tiers = Pairs("t").ToHashSet();
        var options = Pairs("o").ToHashSet();
        foreach (var m in Models().Where(m => m.GetProperty("variant").ValueKind == JsonValueKind.Object))
        {
            var name = m.GetProperty("name").GetString()!;
            var tier = m.GetProperty("variant").GetProperty("tier").GetString()!;
            var opts = m.GetProperty("variant").GetProperty("options").EnumerateArray().Select(o => o.GetProperty("name").GetString()!).ToList();
            var (colour, main) = ProductGenerator.TierNames(name, tier, opts);
            var expected = opts.Zip(main.Options).ToList();
            if (colour is not null)
            {
                tiers.Should().Contain(("Màu Sắc", colour.Name));
                expected.Add((ProductGenerator.ColourOf(name)!, colour.Options[0]));
            }
            if (main.Name != tier) tiers.Should().Contain((tier, main.Name));
            foreach (var (old, @new) in expected.Where(e => e.First != e.Second)) options.Should().Contain((old, @new), name);
        }
    }
}
