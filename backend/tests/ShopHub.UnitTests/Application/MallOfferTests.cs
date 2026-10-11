using FluentAssertions;
using ShopHub.Application.Features.Storefront;
using ShopHub.Domain.Promo;

namespace ShopHub.UnitTests.Application;

// G-VIS round 2 (L185): the offer line of a Mall tile was the programme's free-text name ("Quà tặng cho đơn từ
// ₫300.000") and was cut off; it is now built from the rules, short enough for one line
public class MallOfferTests
{
    [Fact]
    public void Gift_names_the_minimum_spend_in_short()
    {
        MallOffer.Of(PromotionType.Gift, 0, 0, 0, 300_000).Should().Be("Quà tặng đơn 300K");
        MallOffer.Of(PromotionType.Gift, 0, 0, 0, 1_500_000).Should().Be("Quà tặng đơn 1,5TR");
        MallOffer.Of(PromotionType.Gift, 0, 0, 0, 0).Should().Be("Có quà tặng");
    }

    [Fact]
    public void Combo_and_add_on_say_what_the_shopper_gets()
    {
        MallOffer.Of(PromotionType.Combo, 2, 1_000, 0, 0).Should().Be("Mua 2 giảm 10%");
        MallOffer.Of(PromotionType.Combo, 3, 0, 20_000, 0).Should().Be("Mua 3 giảm 20K");
        MallOffer.Of(PromotionType.AddOn, 0, 0, 0, 0).Should().Be("Mua kèm deal sốc");
        MallOffer.Of(PromotionType.Discount, 0, 0, 0, 0).Should().BeNull();
    }

    [Fact]
    public void Every_line_fits_the_tile()
    {
        foreach (var type in Enum.GetValues<PromotionType>())
            (MallOffer.Of(type, 99, 9_900, 9_990_000, 99_000_000) ?? "").Length.Should().BeLessThanOrEqualTo(18);
    }
}
