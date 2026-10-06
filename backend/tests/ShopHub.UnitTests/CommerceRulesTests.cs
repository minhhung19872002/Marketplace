using FluentAssertions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Domain.Common;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.UnitTests;

public class OrderStateMachineTests
{
    private static Order NewOrder(PaymentMethod method = PaymentMethod.Simulated) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SH260101ABCDEF", method, "SIM_FAST", null, DateTimeOffset.UtcNow);

    [Fact]
    public void Cod_orders_skip_awaiting_payment_and_online_ones_start_there()
    {
        NewOrder(PaymentMethod.Cod).Status.Should().Be(OrderStatus.PendingConfirmation);
        NewOrder().Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public void Paying_marks_the_order_paid_and_records_history()
    {
        var order = NewOrder();
        OrderStateMachine.Start(order, OrderActor.Buyer, null, DateTimeOffset.UtcNow);

        OrderStateMachine.Transition(order, OrderStatus.PendingConfirmation, OrderActor.Gateway, null, "Đã thanh toán", DateTimeOffset.UtcNow);

        order.PaymentStatus.Should().Be(OrderPaymentStatus.Paid);
        order.PaidAt.Should().NotBeNull();
        order.History.Select(h => h.ToStatus).Should().Equal(OrderStatus.PendingPayment, OrderStatus.PendingConfirmation);
    }

    [Fact]
    public void An_illegal_jump_is_a_vietnamese_business_error()
    {
        var order = NewOrder();
        var act = () => OrderStateMachine.Transition(order, OrderStatus.Delivered, OrderActor.Seller, null, null, DateTimeOffset.UtcNow);

        act.Should().Throw<BusinessRuleException>().WithMessage("Không thể chuyển đơn SH260101ABCDEF từ \"Chờ thanh toán\" sang \"Đã giao\".");
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        var order = NewOrder();
        var act = () => OrderStateMachine.Transition(order, OrderStatus.Cancelled, OrderActor.Buyer, null, " ", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().WithMessage("Huỷ đơn cần có lý do.");
    }

    [Fact]
    public void Order_totals_must_add_up_and_never_go_negative()
    {
        var order = NewOrder();
        order.SetTotals(100_000, 10_000, 5_000, 30_000, 30_000, 1_000, null, 2);
        order.GrandTotal.Should().Be(84_000);

        var act = () => order.SetTotals(10_000, 20_000, 0, 0, 0, 0, null, 2);
        act.Should().Throw<BusinessRuleException>();
    }
}

public class VoucherRuleTests
{
    private static Voucher Platform() => new(VoucherOwner.Platform, null, "sale12", "Giảm 12%");

    [Fact]
    public void Codes_are_upper_case_letters_and_digits()
    {
        Platform().Code.Should().Be("SALE12");
        var act = () => new Voucher(VoucherOwner.Platform, null, "sale 12%", "x");
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Percent_vouchers_need_a_cap_and_a_valid_period()
    {
        var now = DateTimeOffset.UtcNow;
        var noCap = () => Platform().Configure(VoucherType.Percent, 0, 1200, null, 0, VoucherAudience.Everyone, [], [], now, now.AddDays(1), null, 1, true, VoucherChannel.All);
        var backwards = () => Platform().Configure(VoucherType.Amount, 1000, 0, null, 0, VoucherAudience.Everyone, [], [], now, now.AddDays(-1), null, 1, true, VoucherChannel.All);

        noCap.Should().Throw<BusinessRuleException>().WithMessage("Cần nhập mức giảm tối đa.");
        backwards.Should().Throw<BusinessRuleException>().WithMessage("Thời gian kết thúc phải sau thời gian bắt đầu.");
    }

    [Fact]
    public void Free_shipping_is_platform_only()
    {
        var now = DateTimeOffset.UtcNow;
        var shop = new Voucher(VoucherOwner.Shop, Guid.NewGuid(), "SHOPSHIP", "x");
        var act = () => shop.Configure(VoucherType.FreeShipping, 0, 0, 20_000, 0, VoucherAudience.Everyone, [], [], now, now.AddDays(1), null, 1, true, VoucherChannel.All);
        act.Should().Throw<BusinessRuleException>().WithMessage("Voucher miễn phí vận chuyển chỉ do sàn phát hành.");
    }
}

public class ShippingRuleTests
{
    [Theory]
    [InlineData("79", "79", ShippingZone.SameProvince)]
    [InlineData("79", "74", ShippingZone.SameRegion)]
    [InlineData("01", "79", ShippingZone.CrossRegion)]
    [InlineData("48", "56", ShippingZone.SameRegion)]
    public void Zone_follows_province_and_region(string from, string to, ShippingZone zone) =>
        ShippingCalculator.ZoneOf(from, to).Should().Be(zone);

    [Fact]
    public void Chargeable_weight_is_the_larger_of_actual_and_volumetric()
    {
        // 300 g but a 40×30×20 cm box → 24,000 cm³ / 6000 = 4 kg
        ShippingCalculator.ChargeableWeightG([new ParcelItem(300, 400, 300, 200, 1)]).Should().Be(4_000);
        ShippingCalculator.ChargeableWeightG([new ParcelItem(700, 0, 0, 0, 3)]).Should().Be(2_100);
        ShippingCalculator.ChargeableWeightG([new ParcelItem(10, 0, 0, 0, 1)]).Should().Be(100, "tối thiểu 100 g");
    }

    [Fact]
    public void The_open_ended_band_adds_a_fee_per_extra_500_grams()
    {
        var rate = new ShippingRate(Guid.NewGuid(), ShippingZone.SameRegion, 2000, null, 35_000, 4_000);
        rate.FeeFor(2000).Should().Be(35_000);
        rate.FeeFor(2001).Should().Be(39_000);
        rate.FeeFor(3000).Should().Be(43_000);
    }

    [Fact]
    public void Delivery_dates_skip_sundays_and_holidays()
    {
        // Friday 2026-10-02 + 2 working days: Sat 03, (Sun 04 skipped), Mon 05
        VietnamTime.AddWorkingDays(new DateOnly(2026, 10, 2), 2, new HashSet<DateOnly>()).Should().Be(new DateOnly(2026, 10, 5));
        VietnamTime.AddWorkingDays(new DateOnly(2026, 10, 2), 2, new HashSet<DateOnly> { new(2026, 10, 3) }).Should().Be(new DateOnly(2026, 10, 6));
    }
}
