using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.SourceScan;

/// <summary>
/// L192: migration SentenceCaseSeedNames renames what earlier seeds wrote. Its pairs must be exactly what the seeder writes
/// now (SeedText.SentenceCase) and cover every category name of the seed that changes — a missed one stays Title Case
/// on old databases only, which no other test would see.
/// </summary>
public class SentenceCaseMigrationTests
{
    private static readonly string Migration = File.ReadAllText(Directory.GetFiles(
        Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Persistence", "Migrations"), "*_SentenceCaseSeedNames.cs").Single());

    private static List<(string Old, string New)> UpPairs()
    {
        var up = Migration[..Migration.IndexOf("protected override void Down", StringComparison.Ordinal)];
        return Regex.Matches(up, @"\('((?:[^']|'')*)', '((?:[^']|'')*)'\)")
            .Select(m => (m.Groups[1].Value.Replace("''", "'"), m.Groups[2].Value.Replace("''", "'"))).ToList();
    }

    private static IEnumerable<string> SeedCategoryNames()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "backend", "src", "ShopHub.Infrastructure", "Seed", "Data",
            "catalog-seed.json")));
        static IEnumerable<string> Walk(JsonElement n) =>
            new[] { n.GetProperty("name").GetString()! }.Concat(n.TryGetProperty("children", out var c) ? c.EnumerateArray().SelectMany(Walk) : []);
        return doc.RootElement.GetProperty("categories").EnumerateArray().SelectMany(Walk).Distinct().ToList();
    }

    [Fact]
    public void Every_pair_is_what_the_seeder_writes_now()
    {
        var pairs = UpPairs();
        pairs.Should().NotBeEmpty();
        pairs.Where(p => SeedText.SentenceCase(p.Old) != p.New).Should().BeEmpty();
    }

    [Fact]
    public void Every_seed_category_name_that_changes_is_renamed()
    {
        var renamed = UpPairs().Select(p => p.Old).ToHashSet();
        SeedCategoryNames().Where(n => SeedText.SentenceCase(n) != n && !renamed.Contains(n)).Should().BeEmpty();
    }

    [Fact]
    public void Every_old_shortcut_label_is_renamed_to_the_seeders_label()
    {
        var renamedTo = UpPairs().Select(p => p.New).ToHashSet();
        MarketingSeeder.Shortcuts.Select(s => s.Label).Where(l => l.Contains(' ') && l != "ShopHub Mall").Where(l => !renamedTo.Contains(l))
            .Should().BeEmpty();
    }
}
