using FluentAssertions;
using ShopHub.Application.Features.Checkout;
using ShopHub.Domain.Common;
using ShopHub.Domain.Promo;

namespace ShopHub.UnitTests;

public class PricingEngineTests
{
    private static readonly Guid ShopA = Guid.NewGuid();
    private static readonly Guid ShopB = Guid.NewGuid();
    private static readonly Guid Shirts = Guid.NewGuid();
    private static readonly Guid Phones = Guid.NewGuid();

    private static PricingLine Line(Guid shop, long price, int qty, Guid? category = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), category ?? Shirts, shop, price, qty);

    private static PricingVoucher Voucher(VoucherOwner owner, VoucherType type, long value = 0, int bp = 0, long? max = null,
        long minOrder = 0, Guid? shop = null, Guid[]? categories = null) =>
        new(Guid.NewGuid(), "MA" + Random.Shared.Next(1000, 9999), owner, shop, type, value, bp, max, minOrder, categories ?? [], []);

    private static PricingResult Price(IReadOnlyList<PricingLine> lines, Dictionary<Guid, long>? shipping = null,
        Dictionary<Guid, PricingVoucher>? shopVouchers = null, PricingVoucher? freeship = null, PricingVoucher? platform = null,
        long coins = 0, int coinMaxBp = 5000) =>
        PricingEngine.Price(new PricingInput(lines, shipping ?? [], shopVouchers ?? [], freeship, platform, coins, coinMaxBp));

    private static void AssertConsistent(PricingResult r)
    {
        foreach (var s in r.Shops)
        {
            s.Lines.Sum(l => l.ShopDiscount).Should().Be(s.ShopDiscount);
            s.Lines.Sum(l => l.PlatformDiscount).Should().Be(s.PlatformDiscount);
            s.Lines.Sum(l => l.CoinDiscount).Should().Be(s.CoinUsed);
            s.Lines.Should().OnlyContain(l => l.Payable >= 0);
            s.ShippingDiscount.Should().BeLessThanOrEqualTo(s.ShippingFee);
            s.GrandTotal.Should().Be(s.Lines.Sum(l => l.Payable) + s.ShippingFee - s.ShippingDiscount);
        }
        r.GrandTotal.Should().Be(r.Subtotal - r.ShopDiscount + r.ShippingFee - r.ShippingDiscount - r.PlatformDiscount - r.CoinUsed);
    }

    [Fact]
    public void Two_shops_without_vouchers_add_goods_and_shipping()
    {
        var r = Price([Line(ShopA, 100_000, 2), Line(ShopB, 50_000, 1)], new() { [ShopA] = 30_000, [ShopB] = 16_000 });

        r.Shops.Should().HaveCount(2);
        r.Subtotal.Should().Be(250_000);
        r.ShippingFee.Should().Be(46_000);
        r.GrandTotal.Should().Be(296_000);
        AssertConsistent(r);
    }

    [Fact]
    public void Shop_voucher_only_touches_its_own_shop_and_respects_the_cap()
    {
        var voucher = Voucher(VoucherOwner.Shop, VoucherType.Percent, bp: 2000, max: 30_000, shop: ShopA);
        var r = Price([Line(ShopA, 100_000, 1), Line(ShopA, 100_000, 1), Line(ShopB, 500_000, 1)],
            shopVouchers: new() { [ShopA] = voucher });

        var a = r.Shops.Single(s => s.ShopId == ShopA);
        a.ShopDiscount.Should().Be(30_000, "20% của 200.000 là 40.000 nhưng trần 30.000");
        a.Lines.Select(l => l.ShopDiscount).Should().Equal(15_000, 15_000);
        r.Shops.Single(s => s.ShopId == ShopB).ShopDiscount.Should().Be(0);
        AssertConsistent(r);
    }

    [Fact]
    public void Minimum_order_not_met_says_how_much_more_to_buy()
    {
        var voucher = Voucher(VoucherOwner.Platform, VoucherType.Amount, value: 50_000, minOrder: 250_000);
        var r = Price([Line(ShopA, 215_000, 1)], platform: voucher);

        r.PlatformDiscount.Should().Be(0);
        r.Vouchers.Single().Problem.Should().Be("Mua thêm ₫35.000 để dùng mã này.");
        r.GrandTotal.Should().Be(215_000);
    }

    [Fact]
    public void Platform_discount_is_computed_after_shop_discounts_and_split_by_largest_remainder()
    {
        var shopVoucher = Voucher(VoucherOwner.Shop, VoucherType.Amount, value: 30_000, shop: ShopA);
        var platform = Voucher(VoucherOwner.Platform, VoucherType.Percent, bp: 1000, max: 1_000_000);
        var r = Price([Line(ShopA, 100_000, 1), Line(ShopA, 100_000, 1), Line(ShopA, 100_000, 1)],
            shopVouchers: new() { [ShopA] = shopVoucher }, platform: platform);

        var shop = r.Shops.Single();
        shop.ShopDiscount.Should().Be(30_000);
        // 10% of (300.000 − 30.000) = 27.000, not 10% of 300.000
        shop.PlatformDiscount.Should().Be(27_000);
        shop.Lines.Select(l => l.ShopDiscount).Should().Equal(10_000, 10_000, 10_000);
        shop.Lines.Select(l => l.PlatformDiscount).Should().Equal(9_000, 9_000, 9_000);
        AssertConsistent(r);
    }

    [Fact]
    public void Indivisible_amounts_are_split_exactly_with_the_remainder_going_to_the_largest_fractions()
    {
        var platform = Voucher(VoucherOwner.Platform, VoucherType.Amount, value: 100);
        var r = Price([Line(ShopA, 1_000, 1), Line(ShopA, 1_000, 1), Line(ShopB, 1_000, 1)], platform: platform);

        r.Shops.SelectMany(s => s.Lines).Select(l => l.PlatformDiscount).Should().Equal(34, 33, 33);
        r.PlatformDiscount.Should().Be(100);
        AssertConsistent(r);
    }

    [Fact]
    public void Free_shipping_is_capped_and_split_by_each_shops_fee()
    {
        var freeship = Voucher(VoucherOwner.Platform, VoucherType.FreeShipping, max: 30_000);
        var r = Price([Line(ShopA, 100_000, 1), Line(ShopB, 100_000, 1)], new() { [ShopA] = 30_000, [ShopB] = 10_000 }, freeship: freeship);

        r.ShippingDiscount.Should().Be(30_000);
        r.Shops.Single(s => s.ShopId == ShopA).ShippingDiscount.Should().Be(22_500);
        r.Shops.Single(s => s.ShopId == ShopB).ShippingDiscount.Should().Be(7_500);
        AssertConsistent(r);
    }

    [Fact]
    public void Free_shipping_never_exceeds_the_fee()
    {
        var freeship = Voucher(VoucherOwner.Platform, VoucherType.FreeShipping, max: 50_000);
        var r = Price([Line(ShopA, 100_000, 1)], new() { [ShopA] = 16_000 }, freeship: freeship);

        r.ShippingDiscount.Should().Be(16_000);
        r.GrandTotal.Should().Be(100_000);
    }

    [Fact]
    public void Coins_are_capped_by_the_percentage_and_by_the_balance()
    {
        var lines = new[] { Line(ShopA, 100_000, 1), Line(ShopB, 300_000, 1) };

        var byPercent = Price(lines, coins: 1_000_000, coinMaxBp: 1000);
        var byBalance = Price(lines, coins: 7_777, coinMaxBp: 1000);

        byPercent.CoinUsed.Should().Be(40_000, "tối đa 10% của 400.000");
        byPercent.Shops.Select(s => s.CoinUsed).Should().Equal(10_000, 30_000);
        byBalance.CoinUsed.Should().Be(7_777);
        byBalance.Shops.Sum(s => s.CoinUsed).Should().Be(7_777);
        AssertConsistent(byPercent);
        AssertConsistent(byBalance);
    }

    [Fact]
    public void A_voucher_larger_than_the_order_brings_it_to_zero_not_below()
    {
        var shopVoucher = Voucher(VoucherOwner.Shop, VoucherType.Amount, value: 500_000, shop: ShopA);
        var platform = Voucher(VoucherOwner.Platform, VoucherType.Amount, value: 500_000);
        var r = Price([Line(ShopA, 120_000, 1)], new() { [ShopA] = 20_000 }, new() { [ShopA] = shopVoucher }, platform: platform, coins: 99_999);

        r.ShopDiscount.Should().Be(120_000);
        r.PlatformDiscount.Should().Be(0);
        r.CoinUsed.Should().Be(0);
        r.GrandTotal.Should().Be(20_000, "chỉ còn phí vận chuyển");
        AssertConsistent(r);
    }

    [Fact]
    public void Category_scope_only_counts_matching_lines_towards_minimum_and_discount()
    {
        var voucher = Voucher(VoucherOwner.Platform, VoucherType.Percent, bp: 5000, max: 1_000_000, minOrder: 150_000, categories: [Phones]);
        var cheapPhone = Price([Line(ShopA, 100_000, 1, Phones), Line(ShopA, 900_000, 1, Shirts)], platform: voucher);
        var phone = Price([Line(ShopA, 200_000, 1, Phones), Line(ShopA, 900_000, 1, Shirts)], platform: voucher);

        cheapPhone.Vouchers.Single().Problem.Should().Be("Mua thêm ₫50.000 để dùng mã này.", "áo không tính vào đơn tối thiểu");
        phone.PlatformDiscount.Should().Be(100_000);
        phone.Shops.Single().Lines.Select(l => l.PlatformDiscount).Should().Equal(100_000, 0);
    }

    [Fact]
    public void Coin_cashback_does_not_lower_the_price()
    {
        var cashback = Voucher(VoucherOwner.Platform, VoucherType.CoinCashback, bp: 1000, max: 20_000);
        var r = Price([Line(ShopA, 500_000, 1)], platform: cashback);

        r.GrandTotal.Should().Be(500_000);
        r.CoinCashback.Should().Be(20_000);
        r.Vouchers.Single().Applied.Should().BeTrue();
    }

    [Fact]
    public void Empty_checkout_is_rejected_in_vietnamese()
    {
        var act = () => Price([]);
        act.Should().Throw<BusinessRuleException>().WithMessage("Chưa chọn sản phẩm nào để thanh toán.");
    }
}
