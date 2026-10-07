using ShopHub.Application.Features.Returns;
using ShopHub.Domain.Common;

namespace ShopHub.Application.Features.Finance;

public record SettlementLine(Guid OrderItemId, long LineTotal, int Quantity, long ShopDiscount, long PlatformDiscount, int FixedFeeBp, int ServiceFeeBp);

public record SettlementReturnItem(Guid OrderItemId, int Quantity, long RefundAmount, long RefundCoins);

/// <summary>
/// A refunded return of the order: what was asked (the units' paid share) and what was finally given. A parcel that came
/// back undelivered also gives back its net shipping (<see cref="ShippingRefund"/>, inside the amounts) and the platform's
/// free-shipping share on it; the carrier is owed nothing for it.
/// </summary>
public record SettlementReturn(Guid ReturnId, DateTimeOffset RefundedAt, long RequestedAmount, long RequestedCoins, long RefundAmount, long RefundCoins,
    IReadOnlyList<SettlementReturnItem> Items, long ShippingRefund = 0, long ShippingDiscountBack = 0, bool PlatformBorne = false)
{
    public long ShippingFeeBack => ShippingRefund + ShippingDiscountBack;
}

public record SettlementInput(
    long GrandTotal,
    long ShippingFee,
    long ShippingDiscount,
    long PlatformDiscount,
    long CoinUsed,
    int PaymentFeeBp,
    IReadOnlyList<SettlementLine> Lines,
    IReadOnlyList<SettlementReturn> Returns);

public record SettlementLineResult(Guid OrderItemId, long Goods, long ShopDiscount, long RefundsBorne, long FixedFee, long ServiceFee);

/// <summary>What a completed order is worth to the shop and to everyone else, to the đồng.</summary>
public record SettlementBreakdown(
    long Goods,
    long ShopDiscount,
    long RefundsBorne,
    long FixedFee,
    long PaymentFee,
    long ServiceFee,
    long Net,
    long MoneyKept,
    long Subsidy,
    long ShippingFee,
    IReadOnlyList<SettlementLineResult> Lines)
{
    public long Fees => FixedFee + PaymentFee + ServiceFee;
}

/// <summary>
/// Shop earnings of an order (spec 3.9):
/// <c>net = goods − shop discounts − refunds the shop bears − fixed fee (% by category) − payment fee − service fee</c>.
/// Platform vouchers, xu and freeship are the platform's subsidy and never reduce the shop's earnings (spec 3.6).
/// A refund of whole units (the buyer got back exactly what was paid for them) costs the shop those units' value after
/// its own discount — the money and xu refunded plus the platform discount on them, which the platform stops
/// subsidising. A partial offer (less than the units' share) costs the shop exactly what was given. Fees are charged on
/// what the shop keeps. Pure function: the posting built from it always balances
/// (<c>money kept + subsidy = net + fees + shipping fee</c>).
/// </summary>
public static class SettlementCalculator
{
    public static SettlementBreakdown Compute(SettlementInput input)
    {
        var lines = input.Lines.ToDictionary(l => l.OrderItemId);
        var borne = input.Lines.ToDictionary(l => l.OrderItemId, _ => 0L);
        var returned = input.Lines.ToDictionary(l => l.OrderItemId, _ => 0);
        long moneyRefunded = 0, subsidyCancelled = 0, shippingBack = 0, platformRefunds = 0;

        foreach (var r in input.Returns.OrderBy(r => r.RefundedAt).ThenBy(r => r.ReturnId))
        {
            if (r.PlatformBorne)
            {
                // The platform pays the refund out of its subsidy: the shop's earnings and fees stay as they were; the
                // xu given back were already the platform's subsidy, so only the money moves
                moneyRefunded += r.RefundAmount;
                subsidyCancelled -= r.RefundAmount;
                platformRefunds += r.RefundAmount;
                continue;
            }
            moneyRefunded += r.RefundAmount;
            subsidyCancelled += r.RefundCoins + r.ShippingDiscountBack;
            shippingBack += r.ShippingFeeBack;
            var whole = r.RefundAmount >= r.RequestedAmount && r.RefundCoins >= r.RequestedCoins;
            // The goods' part only: the shipping given back is not the shop's cost
            var cost = r.RefundAmount + r.RefundCoins - r.ShippingRefund;
            var weights = r.Items.Select(i => i.RefundAmount + i.RefundCoins).ToList();
            var shares = Money.Vnd(cost).Allocate(weights.All(w => w == 0) ? r.Items.Select(_ => 1L).ToList() : weights);
            for (var k = 0; k < r.Items.Count; k++)
            {
                var item = r.Items[k];
                var line = lines[item.OrderItemId];
                borne[item.OrderItemId] += shares[k].Value;
                if (!whole) continue;
                // The units are back: the platform no longer pays its discount on them
                var platformShare = ReturnPricing.ForUnits(line.PlatformDiscount, 0, line.Quantity, returned[item.OrderItemId], item.Quantity).Money;
                borne[item.OrderItemId] += platformShare;
                subsidyCancelled += platformShare;
                returned[item.OrderItemId] += item.Quantity;
            }
        }

        var results = input.Lines.Select(l =>
        {
            var basis = l.LineTotal - l.ShopDiscount - borne[l.OrderItemId];
            if (basis < 0) throw new InvalidOperationException($"Refunds on line {l.OrderItemId} exceed what the shop received for it.");
            return new SettlementLineResult(l.OrderItemId, l.LineTotal, l.ShopDiscount, borne[l.OrderItemId],
                Money.Vnd(basis).PercentBp(l.FixedFeeBp).Value, Money.Vnd(basis).PercentBp(l.ServiceFeeBp).Value);
        }).ToList();

        var moneyKept = input.GrandTotal - moneyRefunded;
        if (moneyKept < 0) throw new InvalidOperationException("More money was refunded than the buyer paid.");
        var goods = results.Sum(r => r.Goods);
        var shopDiscount = results.Sum(r => r.ShopDiscount);
        var refundsBorne = results.Sum(r => r.RefundsBorne);
        var fixedFee = results.Sum(r => r.FixedFee);
        var serviceFee = results.Sum(r => r.ServiceFee);
        // A refund the platform pays itself does not lower the shop's payment fee either
        var paymentFee = Money.Vnd(moneyKept + platformRefunds).PercentBp(input.PaymentFeeBp).Value;
        // Fees never take more than what the shop keeps
        var fees = fixedFee + serviceFee + paymentFee;
        var gross = goods - shopDiscount - refundsBorne;
        if (fees > gross)
        {
            paymentFee = Math.Max(0, paymentFee - (fees - gross));
            fees = fixedFee + serviceFee + paymentFee;
            if (fees > gross) throw new InvalidOperationException("Fee rates above 100% of the goods value.");
        }
        var net = gross - fees;
        var subsidy = input.PlatformDiscount + input.CoinUsed + input.ShippingDiscount - subsidyCancelled;
        var shipping = input.ShippingFee - shippingBack;
        if (shipping < 0) throw new InvalidOperationException("More shipping was given back than charged.");
        if (moneyKept + subsidy != net + fees + shipping)
            throw new InvalidOperationException(
                $"Settlement does not balance: kept {moneyKept} + subsidy {subsidy} ≠ net {net} + fees {fees} + shipping {shipping}.");
        return new SettlementBreakdown(goods, shopDiscount, refundsBorne, fixedFee, paymentFee, serviceFee, net, moneyKept, subsidy,
            shipping, results);
    }
}
