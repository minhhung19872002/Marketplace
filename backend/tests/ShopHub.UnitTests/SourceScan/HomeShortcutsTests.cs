using System.Text.RegularExpressions;
using FluentAssertions;
using ShopHub.Infrastructure.Seed;

namespace ShopHub.UnitTests.SourceScan;

/// <summary>
/// E3 (spec II.1): the seeded home shortcuts include Mã giảm giá, Freeship and Deal sốc, the "Freeship" banner opens the
/// freeship filter, and every seeded link is a page the buyer site really has (a route in web/src/App.tsx).
/// </summary>
public class HomeShortcutsTests
{
    private static readonly List<Regex> Routes = ParseRoutes(File.ReadAllText(Path.Combine(RepoFiles.RepoRoot, "web", "src", "App.tsx")));

    /// <summary>Full paths of the router, nested routes included (<c>/tai-khoan</c> + <c>voucher</c>).</summary>
    private static List<Regex> ParseRoutes(string app)
    {
        var parents = new Stack<string>();
        var paths = new List<string>();
        // One route per line; a line ending in "/>" closes itself, one ending in ">" opens a parent (element={<X />} inside is fine)
        foreach (Match m in Regex.Matches(app, @"<Route\b(?:[^\n]*?\bpath=""(?<path>[^""]*)"")?[^\n]*?(?<self>/)?>[ \t]*\r?$|</Route>", RegexOptions.Multiline))
        {
            if (m.Value == "</Route>")
            {
                if (parents.Count > 0) parents.Pop();
                continue;
            }
            var parent = parents.Count > 0 ? parents.Peek() : "";
            var path = m.Groups["path"].Value;
            var full = path.StartsWith('/') ? path : $"{parent.TrimEnd('/')}/{path}";
            if (m.Groups["path"].Success) paths.Add(full.Length > 1 ? full.TrimEnd('/') : full);
            if (!m.Groups["self"].Success) parents.Push(m.Groups["path"].Success ? full : parent);
        }
        return paths.Select(p => new Regex("^" + Regex.Replace(p, @":\w+", "[^/]+") + "$")).ToList();
    }

    private static bool IsPage(string link) => Routes.Any(r => r.IsMatch(link.Split('?')[0]));

    [Fact]
    public void Shortcuts_cover_vouchers_freeship_and_deals()
    {
        MarketingSeeder.Shortcuts.Select(s => s.Label).Should().Contain(["Mã Giảm Giá", "Freeship", "Deal Sốc"]);
        MarketingSeeder.Shortcuts.Single(s => s.Label == "Freeship").Link.Should().Contain("freeship=true");
    }

    [Fact]
    public void Side_banners_do_not_repeat_what_a_hero_slide_says()
    {
        // G-VIS round 2: the hero and the second side banner both said "Freeship ₫30K" — the three places at the top of
        // the home page now each say something else
        static string Theme(string title) => title.Split(" — ")[0].Trim().ToLowerInvariant();
        var heroThemes = MarketingSeeder.MainBanners.Select(b => Theme(b.Title)).ToHashSet();
        MarketingSeeder.SideBanners.Select(b => Theme(b.Title)).Should().NotIntersectWith(heroThemes);
        MarketingSeeder.SideBanners.Should().NotContain(b => b.Title.Contains("Freeship", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_seeded_home_link_is_a_page_of_the_buyer_site()
    {
        Routes.Should().HaveCountGreaterThan(20);
        IsPage("/tai-khoan/voucher").Should().BeTrue("route lồng dưới /tai-khoan cũng được đọc");
        IsPage("/khong-co-trang-nay").Should().BeFalse();
        var links = MarketingSeeder.Shortcuts.Select(s => s.Link).Concat(MarketingSeeder.SideBanners.Select(b => b.Link))
            .Concat(MarketingSeeder.MainBanners.Select(b => b.Link));
        links.Where(l => !IsPage(l)).Should().BeEmpty("lối tắt / banner phải trỏ tới trang thật của site người mua");
    }
}
