using FluentAssertions;
using ShopHub.Application.Features.Storefront;

namespace ShopHub.UnitTests.Application;

// G-VIS round 2 (L186): "Gợi ý hôm nay" showed the same item 2–3 times in 18 cards (one model, several shops) and runs of
// one industry; the feed, "Sản phẩm đã xem" and the Flash Sale now keep one listing per item and alternate industries
public class SpreadTests
{
    private static readonly Guid Home = Guid.NewGuid(), Fashion = Guid.NewGuid(), Tech = Guid.NewGuid();

    private static List<(string Name, Guid? Industry)> Run(params (string, Guid?)[] items) =>
        ProductCards.Spread(items, i => ProductCards.ModelKey(i.Item1), i => i.Item2).Select(i => (i.Item1, i.Item2)).ToList();

    [Fact]
    public void Keeps_one_listing_per_item()
    {
        var result = Run(("Dao Bào Vỏ Rau Củ", Home), ("Áo Bomber Đỏ", Fashion), ("Dao Bào Vỏ Rau Củ - Giao Nhanh", Home),
            ("dao bào vỏ rau củ", Home), ("Tai Nghe", Tech));
        result.Select(r => r.Name).Should().Equal("Dao Bào Vỏ Rau Củ", "Áo Bomber Đỏ", "Tai Nghe");
    }

    [Fact]
    public void Never_puts_two_neighbours_of_one_industry_while_another_is_left()
    {
        var result = Run(("A", Home), ("B", Home), ("C", Home), ("D", Fashion), ("E", Tech), ("F", Fashion));
        result.Select(r => r.Name).Should().Equal("A", "D", "B", "E", "C", "F");
        result.Zip(result.Skip(1)).Should().OnlyContain(p => p.First.Industry != p.Second.Industry);
    }

    [Fact]
    public void Keeps_the_order_when_only_one_industry_is_left()
    {
        Run(("A", Home), ("B", Home), ("C", Home)).Select(r => r.Name).Should().Equal("A", "B", "C");
        Run(("A", null), ("B", null)).Select(r => r.Name).Should().Equal("A", "B");
    }
}
