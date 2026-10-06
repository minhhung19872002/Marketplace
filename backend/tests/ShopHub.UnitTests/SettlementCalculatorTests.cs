using FluentAssertions;
using ShopHub.Application.Features.Finance;

namespace ShopHub.UnitTests;

public class SettlementCalculatorTests
{
    private static readonly Guid LineA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid LineB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    // 3 × ₫99.999 + 1 × ₫150.000; shop voucher ₫20.000, platform voucher ₫50.000, 7.000 xu, ship ₫30.000 of which ₫15.000 freeship
    private static SettlementInput Order(params SettlementReturn[] returns)
    {
        SettlementLine[] lines =
        [
            new(LineA, 299_997, 3, 13_334, 33_333, 500, 0),
            new(LineB, 150_000, 1, 6_666, 16_667, 300, 100),
        ];
        const long goods = 449_997, shop = 20_000, platform = 50_000, coins = 7_000, ship = 30_000, freeship = 15_000;
        return new SettlementInput(goods - shop - platform - coins + ship - freeship, ship, freeship, platform, coins, 200, lines, returns);
    }

    [Fact]
    public void Net_is_goods_minus_shop_discounts_and_fees_and_the_posting_balances()
    {
        var b = SettlementCalculator.Compute(Order());

        b.Goods.Should().Be(449_997);
        b.ShopDiscount.Should().Be(20_000);
        b.FixedFee.Should().Be(14_333 + 4_300);          // 5% of 286.663 (rounded) + 3% of 143.334 (rounded)
        b.ServiceFee.Should().Be(1_433);                  // 1% of 143.334 on line B only
        b.PaymentFee.Should().Be(7_760);                  // 2% of the ₫387.997 the buyer paid
        b.Net.Should().Be(449_997 - 20_000 - 18_633 - 1_433 - 7_760);
        // Platform vouchers, xu and freeship never come out of the shop's money
        b.Subsidy.Should().Be(50_000 + 7_000 + 15_000);
        (b.MoneyKept + b.Subsidy).Should().Be(b.Net + b.Fees + b.ShippingFee);
    }

    [Fact]
    public void Returning_whole_units_costs_the_shop_their_value_after_its_own_discount_and_ends_the_platform_subsidy_on_them()
    {
        // Line A was paid 299.997 − 13.334 − 33.333 − 7.000 xu = 246.330: the first unit's share is 82.110 + 2.334 xu,
        // its platform discount share 11.111
        var r = new SettlementReturn(Guid.NewGuid(), DateTimeOffset.UtcNow, 82_110, 2_334, 82_110, 2_334, [new(LineA, 1, 82_110, 2_334)]);

        var b = SettlementCalculator.Compute(Order(r));

        b.RefundsBorne.Should().Be(82_110 + 2_334 + 11_111);
        b.Subsidy.Should().Be(72_000 - 11_111 - 2_334);
        b.MoneyKept.Should().Be(387_997 - 82_110);
        b.Lines.Single(l => l.OrderItemId == LineA).FixedFee.Should().Be(9_555); // 5% of 299.997 − 13.334 − 95.555
        (b.MoneyKept + b.Subsidy).Should().Be(b.Net + b.Fees + b.ShippingFee);
    }

    [Fact]
    public void A_partial_offer_costs_the_shop_exactly_what_was_given()
    {
        var r = new SettlementReturn(Guid.NewGuid(), DateTimeOffset.UtcNow, 82_110, 2_334, 40_000, 0, [new(LineA, 1, 82_110, 2_334)]);

        var b = SettlementCalculator.Compute(Order(r));

        b.RefundsBorne.Should().Be(40_000);
        b.Subsidy.Should().Be(72_000);
        (b.MoneyKept + b.Subsidy).Should().Be(b.Net + b.Fees + b.ShippingFee);
    }

    [Fact]
    public void Returning_everything_unit_by_unit_leaves_nothing_for_the_shop_and_no_subsidy_but_freeship()
    {
        var at = DateTimeOffset.UtcNow;
        long[] money = [82_110, 82_110, 82_110];
        long[] xu = [2_334, 2_333, 2_333];
        var returns = Enumerable.Range(0, 3)
            .Select(i => new SettlementReturn(Guid.NewGuid(), at.AddMinutes(i), money[i], xu[i], money[i], xu[i], [new(LineA, 1, money[i], xu[i])]))
            .Append(new SettlementReturn(Guid.NewGuid(), at.AddMinutes(9), 126_667, 0, 126_667, 0, [new(LineB, 1, 126_667, 0)]))
            .ToArray();

        var b = SettlementCalculator.Compute(Order(returns));

        b.RefundsBorne.Should().Be(449_997 - 20_000);
        b.Net.Should().Be(0);
        b.Fees.Should().Be(0);
        // What is left is the shipping the buyer paid and the platform's freeship
        b.MoneyKept.Should().Be(15_000);
        b.Subsidy.Should().Be(15_000);
        (b.MoneyKept + b.Subsidy).Should().Be(b.Net + b.Fees + b.ShippingFee);
    }

    [Fact]
    public void Fees_never_take_more_than_the_shop_keeps()
    {
        var input = new SettlementInput(1_000, 0, 0, 0, 0, 5_000, [new(LineA, 1_000, 1, 0, 0, 5_000, 5_000)], []);

        var b = SettlementCalculator.Compute(input);

        b.Net.Should().Be(0);
        b.Fees.Should().Be(1_000);
    }
}
