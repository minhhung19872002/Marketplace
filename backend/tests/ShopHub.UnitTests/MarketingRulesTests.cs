using FluentAssertions;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Marketing;
using ShopHub.Domain.Promo;

namespace ShopHub.UnitTests;

public class MarketingRulesTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Cup = Guid.NewGuid(), Mug = Guid.NewGuid(), Plate = Guid.NewGuid();

    private static PricingLine Line(Guid product, long price, int qty) => new(Guid.NewGuid(), product, Guid.NewGuid(), Shop, price, qty);

    [Fact]
    public void A_combo_takes_its_percent_off_the_listed_products_once_the_quantity_is_reached_split_by_largest_remainder()
    {
        // Buy 3 of {cup, mug} → 10%: 2 × 100.001 + 1 × 50.000 = 250.002 → 25.000 off
        var lines = new[] { Line(Cup, 100_001, 2), Line(Mug, 50_000, 1), Line(Plate, 80_000, 1) };
        var combo = new PricingCombo(Guid.NewGuid(), Shop, [Cup, Mug], 3, 1_000, 0);

        var r = PricingEngine.Price(new PricingInput(lines, new Dictionary<Guid, long>(), new Dictionary<Guid, PricingVoucher>(), null, null, 0, 0, [combo]));

        var priced = r.Shops.Single().Lines;
        priced.Sum(l => l.ComboDiscount).Should().Be(25_000);
        priced.Single(l => l.Line.ProductId == Plate).ComboDiscount.Should().Be(0);
        priced.Where(l => l.ComboDiscount > 0).Should().OnlyContain(l => l.ComboId == combo.PromotionId);
        r.GrandTotal.Should().Be(330_002 - 25_000);
    }

    [Fact]
    public void Below_the_combo_quantity_nothing_changes_and_a_line_never_counts_for_two_combos()
    {
        var lines = new[] { Line(Cup, 100_000, 2), Line(Mug, 50_000, 1) };
        var big = new PricingCombo(Guid.NewGuid(), Shop, [Cup, Mug], 3, 2_000, 0);
        var small = new PricingCombo(Guid.NewGuid(), Shop, [Cup], 2, 500, 0);
        var tooMany = new PricingCombo(Guid.NewGuid(), Shop, [Cup, Mug], 4, 5_000, 0);

        var r = PricingEngine.Price(new PricingInput(lines, new Dictionary<Guid, long>(), new Dictionary<Guid, PricingVoucher>(), null, null, 0, 0,
            [small, big, tooMany]));

        // The 20% combo saves more (50.000) than 5% on the cups (10.000): it takes the cups, the small one gets nothing
        r.ComboDiscount.Should().Be(50_000);
        r.Shops.Single().Lines.Should().OnlyContain(l => l.ComboId == big.PromotionId);
    }

    [Fact]
    public void The_shop_voucher_applies_to_what_is_left_after_the_combo()
    {
        var lines = new[] { Line(Cup, 100_000, 3) };
        var combo = new PricingCombo(Guid.NewGuid(), Shop, [Cup], 3, 0, 30_000);
        var voucher = new PricingVoucher(Guid.NewGuid(), "SHOP10", VoucherOwner.Shop, Shop, VoucherType.Percent, 0, 1_000, null, 0, [], []);

        var r = PricingEngine.Price(new PricingInput(lines, new Dictionary<Guid, long>(), new Dictionary<Guid, PricingVoucher> { [Shop] = voucher }, null, null,
            0, 0, [combo]));

        r.ComboDiscount.Should().Be(30_000);
        r.ShopDiscount.Should().Be(27_000);   // 10% of 270.000
        r.GrandTotal.Should().Be(243_000);
    }

    private static CoinEntry Credit(long coins, DateTimeOffset? expires, DateTimeOffset at) =>
        new(Guid.Empty, coins, CoinReason.CheckIn, null, null, expires, null, at);

    private static CoinEntry Spend(long coins, DateTimeOffset at) => new(Guid.Empty, -coins, CoinReason.CheckoutSpend, null, null, null, null, at);

    [Fact]
    public void Spending_uses_the_xu_that_expire_first_and_only_the_unspent_rest_of_an_expired_credit_is_written_off()
    {
        var now = DateTimeOffset.UtcNow;
        CoinEntry[] entries =
        [
            Credit(500, now.AddDays(-1), now.AddDays(-30)),   // expired yesterday
            Credit(300, now.AddDays(10), now.AddDays(-20)),   // still valid
            Credit(200, null, now.AddDays(-10)),              // never expires
            Spend(350, now.AddDays(-5)),                      // taken from the 500 that expired first
        ];

        CoinExpiryService.Expirable(entries, now).Should().Be(150);
        CoinExpiryService.Expirable(entries, now.AddDays(11)).Should().Be(150 + 300);
        CoinExpiryService.Expirable([.. entries, Spend(700, now)], now.AddDays(11)).Should().Be(0);
    }

    [Theory]
    [InlineData(10, 3, 2, 3)]      // 3.33 kept / 6.67 taken back → 3 / 7 (Math.Floor kept 4)
    [InlineData(100, 3, 1, 67)]
    [InlineData(10, 4, 1, 8)]
    [InlineData(500, 200_000, 0, 500)]
    [InlineData(500, 200_000, 250_000, 0)]
    public void Cash_back_kept_after_a_refund_is_split_by_the_largest_remainder(long share, long goods, long refunded, long kept)
    {
        var k = CashbackService.Kept(share, goods, refunded);
        k.Should().Be(kept);
        (share - k).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Member_segments_are_exact_tiers_of_active_accounts()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), locked = Guid.NewGuid();
        var spend = new Dictionary<Guid, long> { [a] = 2_500_000, [b] = 12_000_000, [c] = 900_000, [locked] = 3_000_000 };
        var active = new HashSet<Guid> { a, b, c };
        ShopHub.Application.Features.Chat.SendBroadcastHandler.MemberSegment(ShopHub.Domain.Engage.BroadcastSegment.MemberGold, spend, active, 2_000_000, 10_000_000)
            .Should().Equal(a);
        ShopHub.Application.Features.Chat.SendBroadcastHandler.MemberSegment(ShopHub.Domain.Engage.BroadcastSegment.MemberDiamond, spend, active, 2_000_000, 10_000_000)
            .Should().Equal(b);
    }
}
