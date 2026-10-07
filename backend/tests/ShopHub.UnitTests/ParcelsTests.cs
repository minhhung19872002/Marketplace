using FluentAssertions;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Finance;

namespace ShopHub.UnitTests;

/// <summary>Đa kho: one shop's parcels quoted together, the order's money shared over them to the đồng.</summary>
public class ParcelsTests
{
    private static ShippingOption Option(string code, long fee, int days, bool cod = true) =>
        new(code, code, null, fee, days, new DateOnly(2026, 10, 7).AddDays(days), cod);

    [Fact]
    public void A_carrier_is_offered_only_when_it_serves_every_parcel_with_the_summed_fee_and_the_slowest_day()
    {
        var combined = Parcels.Combine([
            [Option("NHANH", 16_000, 2), Option("TIETKIEM", 12_000, 4), Option("HOATOC", 60_000, 1)],
            [Option("NHANH", 30_000, 3, cod: false), Option("TIETKIEM", 22_000, 5)],
        ]);

        combined.Select(o => o.Code).Should().Equal("NHANH", "TIETKIEM");
        combined[0].Should().Match<ShippingOption>(o => o.Fee == 46_000 && o.Days == 3 && !o.SupportsCod);
        combined[1].Should().Match<ShippingOption>(o => o.Fee == 34_000 && o.Days == 5 && o.SupportsCod && o.ExpectedDate == new DateOnly(2026, 10, 12));
        Parcels.Combine([[Option("NHANH", 16_000, 2)]]).Single().Fee.Should().Be(16_000);
    }

    [Theory]
    [InlineData(15_000, new long[] { 16_000, 30_000 })]
    [InlineData(10_001, new long[] { 1, 1, 1 })]
    [InlineData(0, new long[] { 16_000, 30_000 })]
    public void The_free_shipping_discount_shared_over_parcels_adds_up_and_never_exceeds_a_parcel_fee(long discount, long[] fees)
    {
        var shares = Parcels.ShareDiscount(Math.Min(discount, fees.Sum()), fees);

        shares.Sum().Should().Be(Math.Min(discount, fees.Sum()));
        shares.Zip(fees).Should().OnlyContain(x => x.First <= x.Second);
    }

    [Fact]
    public void Cod_per_parcel_adds_up_to_the_order_total()
    {
        Parcels.ShareCod(333_334, [100_000, 100_000, 133_334]).Sum().Should().Be(333_334);
        Parcels.ShareCod(10, [3, 3, 3]).Should().Equal(4, 3, 3);
        Parcels.ShareCod(0, [0, 0]).Sum().Should().Be(0);
        Parcels.ShareCod(5_000, [0, 0]).Should().Equal(5_000, 0);
    }

    [Fact]
    public void An_undelivered_parcel_gives_back_its_goods_and_net_shipping_and_the_carrier_is_owed_only_the_rest()
    {
        var lineA = Guid.NewGuid();
        var lineB = Guid.NewGuid();
        // Same order as SettlementCalculatorTests; line B (1 × ₫150.000, paid ₫126.667) travelled alone in a parcel
        // charged ₫12.000 of the ₫30.000 shipping, ₫6.000 of it covered by the free-shipping voucher
        var undelivered = new SettlementReturn(Guid.NewGuid(), DateTimeOffset.UtcNow, 132_667, 0, 132_667, 0, [new(lineB, 1, 126_667, 0)],
            ShippingRefund: 6_000, ShippingDiscountBack: 6_000);
        var input = new SettlementInput(387_997, 30_000, 15_000, 50_000, 7_000, 200,
            [new(lineA, 299_997, 3, 13_334, 33_333, 500, 0), new(lineB, 150_000, 1, 6_666, 16_667, 300, 100)], [undelivered]);

        var b = SettlementCalculator.Compute(input);

        b.RefundsBorne.Should().Be(126_667 + 16_667, "the goods of the parcel at the shop's net, the platform stops subsidising them");
        b.Lines.Single(l => l.OrderItemId == lineB).FixedFee.Should().Be(0);
        b.ShippingFee.Should().Be(18_000);
        b.MoneyKept.Should().Be(387_997 - 132_667);
        b.Subsidy.Should().Be(72_000 - 16_667 - 6_000);
        (b.MoneyKept + b.Subsidy).Should().Be(b.Net + b.Fees + b.ShippingFee);
    }
}
