using FluentAssertions;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Storefront;

namespace ShopHub.UnitTests.Application;

// G3 B2 (L160): the same item listed by several shops must not show twice in one row of a grid
public class DiversifyTests
{
    private static ProductCardDto Card(string name) =>
        new(Guid.NewGuid(), name, "x", null, 1_000, 1_000, 1_000, 0, 0, 0, 0, true, Guid.NewGuid(), "Shop", false, false, null);

    private static int MinGap(IReadOnlyList<ProductCardDto> cards) =>
        cards.Select((c, i) => (Key: ProductCards.ModelKey(c.Name), i)).GroupBy(x => x.Key)
            .SelectMany(g => g.Zip(g.Skip(1), (a, b) => b.i - a.i)).DefaultIfEmpty(int.MaxValue).Min();

    [Fact]
    public void A_shop_wording_after_the_dash_names_the_same_item()
    {
        ProductCards.ModelKey("Nước Ngọt Có Ga Chai 1.5L - Giao Nhanh").Should().Be(ProductCards.ModelKey("nuoc ngot co ga chai 1.5l"));
        ProductCards.ModelKey("Áo Thun Nam").Should().NotBe(ProductCards.ModelKey("Áo Thun Nữ"));
    }

    [Fact]
    public void Copies_of_one_item_are_at_least_a_row_apart_when_the_page_allows_it()
    {
        // Best sellers: the top item listed by four shops comes first four times
        var page = new[] { "A", "A - Giao Nhanh", "A - Cao Cấp", "A - Hàng Có Sẵn" }.Select(Card)
            .Concat(Enumerable.Range(1, 20).Select(i => Card($"Khác {i}"))).ToList();
        MinGap(page).Should().Be(1);

        var spread = ProductCards.Diversify(page);

        spread.Should().HaveCount(page.Count).And.OnlyHaveUniqueItems();
        spread.Select(c => c.Id).Should().BeEquivalentTo(page.Select(c => c.Id));
        MinGap(spread).Should().BeGreaterThanOrEqualTo(6);
        spread[0].Should().Be(page[0], "the best card still leads");
    }

    [Fact]
    public void A_page_of_few_items_is_interleaved_as_far_as_it_can_be()
    {
        var page = Enumerable.Range(0, 8).SelectMany(_ => new[] { "Tai nghe A", "Tai nghe B", "Tai nghe C" }).OrderBy(n => n).Select(Card).ToList();
        var spread = ProductCards.Diversify(page);
        spread.Should().HaveCount(24);
        MinGap(spread).Should().Be(3);
    }
}
