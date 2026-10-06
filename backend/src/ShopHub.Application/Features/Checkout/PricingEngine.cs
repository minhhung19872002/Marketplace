using ShopHub.Domain.Common;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Checkout;

public record PricingLine(Guid SkuId, Guid ProductId, Guid CategoryId, Guid ShopId, long UnitPrice, int Quantity,
    bool FreeshipXtra = false, bool VoucherXtra = false)
{
    public long LineTotal => checked(UnitPrice * Quantity);
}

public record PricingVoucher(
    Guid Id,
    string Code,
    VoucherOwner Owner,
    Guid? ShopId,
    VoucherType Type,
    long DiscountValue,
    int DiscountPercentBp,
    long? MaxDiscount,
    long MinOrder,
    IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> ProductIds,
    bool XtraOnly = false)
{
    public bool Covers(PricingLine line) =>
        (Owner == VoucherOwner.Platform || line.ShopId == ShopId)
        && (CategoryIds.Count == 0 || CategoryIds.Contains(line.CategoryId))
        && (ProductIds.Count == 0 || ProductIds.Contains(line.ProductId))
        && (!XtraOnly || (Type == VoucherType.FreeShipping ? line.FreeshipXtra : line.VoucherXtra));

    public static PricingVoucher From(Voucher v) =>
        new(v.Id, v.Code, v.Owner, v.ShopId, v.Type, v.DiscountValue, v.DiscountPercentBp, v.MaxDiscount, v.MinOrder, v.CategoryIds, v.ProductIds, v.XtraOnly);
}

/// <summary>Shop combo: at least <see cref="MinQuantity"/> units of the listed products → % or amount off those lines (the shop bears it).</summary>
public record PricingCombo(Guid PromotionId, Guid ShopId, IReadOnlyCollection<Guid> ProductIds, int MinQuantity, int DiscountBp, long DiscountAmount);

public record PricingInput(
    IReadOnlyList<PricingLine> Lines,
    IReadOnlyDictionary<Guid, long> ShippingFees,
    IReadOnlyDictionary<Guid, PricingVoucher> ShopVouchers,
    PricingVoucher? FreeshipVoucher,
    PricingVoucher? PlatformVoucher,
    long CoinsAvailable,
    int CoinMaxBp,
    IReadOnlyList<PricingCombo>? Combos = null);

public record PricedLine(PricingLine Line, long ShopDiscount, long PlatformDiscount, long CoinDiscount, long ComboDiscount = 0, Guid? ComboId = null)
{
    public long Payable => Line.LineTotal - ComboDiscount - ShopDiscount - PlatformDiscount - CoinDiscount;
}

public record PricedShop(
    Guid ShopId,
    IReadOnlyList<PricedLine> Lines,
    long Subtotal,
    long ShopDiscount,
    long ShippingFee,
    long ShippingDiscount,
    long PlatformDiscount,
    long CoinUsed,
    Guid? ShopVoucherId,
    long ComboDiscount = 0)
{
    public long GrandTotal => Subtotal - ComboDiscount - ShopDiscount + ShippingFee - ShippingDiscount - PlatformDiscount - CoinUsed;
}

/// <summary>Whether a voucher took effect; when not, <see cref="Problem"/> says why in Vietnamese.</summary>
public record VoucherOutcome(Guid VoucherId, string Code, long Discount, string? Problem)
{
    public bool Applied => Problem is null;
}

public record PricingResult(
    IReadOnlyList<PricedShop> Shops,
    IReadOnlyList<VoucherOutcome> Vouchers,
    long CoinUsed,
    long CoinMax,
    long CoinCashback)
{
    public long Subtotal => Shops.Sum(s => s.Subtotal);
    public long ShopDiscount => Shops.Sum(s => s.ShopDiscount);
    public long ComboDiscount => Shops.Sum(s => s.ComboDiscount);
    public long ShippingFee => Shops.Sum(s => s.ShippingFee);
    public long ShippingDiscount => Shops.Sum(s => s.ShippingDiscount);
    public long PlatformDiscount => Shops.Sum(s => s.PlatformDiscount);
    public long GrandTotal => Shops.Sum(s => s.GrandTotal);
}

/// <summary>
/// The one place money is computed for a checkout (spec 3.6). Order of operations:
/// <code>
/// line totals − shop combo deals − shop voucher (≤ 1 per shop) = shop subtotal
/// + shipping per shop − platform free-shipping voucher (split by shipping fee)
/// − platform discount voucher (split by line value after shop discounts)
/// − ShopHub Xu (≤ CoinMaxBp of the goods value after discounts)
/// = grand total (never below 0)
/// </code>
/// Every split uses the largest remainder method, so the per-line parts add up exactly to each discount.
/// Eligibility that needs the database (time, quota, per-user limit, audience) is checked before; this class
/// only applies minimum-order and product/category scope rules, so it is a pure function.
/// </summary>
public static class PricingEngine
{
    public static PricingResult Price(PricingInput input)
    {
        if (input.Lines.Count == 0) throw new BusinessRuleException("Chưa chọn sản phẩm nào để thanh toán.");
        if (input.Lines.Any(l => l.Quantity < 1 || l.UnitPrice < 0)) throw new BusinessRuleException("Số lượng hoặc đơn giá không hợp lệ.");

        var lines = input.Lines.ToList();
        var comboDiscount = new long[lines.Count];
        var comboOf = new Guid?[lines.Count];
        var shopDiscount = new long[lines.Count];
        var platformDiscount = new long[lines.Count];
        var coinDiscount = new long[lines.Count];
        var outcomes = new List<VoucherOutcome>();
        var shopIds = lines.Select(l => l.ShopId).Distinct().ToList();
        var shopVoucherApplied = new Dictionary<Guid, Guid>();

        // 0) Shop combos: each line counts for one combo at most (the one that saves the buyer most goes first)
        var combos = (input.Combos ?? []).Select(c =>
        {
            var idx = Indexes(lines, i => lines[i].ShopId == c.ShopId && c.ProductIds.Contains(lines[i].ProductId));
            var value = idx.Sum(i => lines[i].LineTotal);
            var qualifies = idx.Sum(i => lines[i].Quantity) >= c.MinQuantity;
            var amount = !qualifies ? 0 : c.DiscountBp > 0 ? Money.Vnd(value).PercentBp(c.DiscountBp).Value : Math.Min(c.DiscountAmount, value);
            return (Combo: c, Amount: amount);
        }).Where(x => x.Amount > 0).OrderByDescending(x => x.Amount).ThenBy(x => x.Combo.PromotionId).ToList();
        foreach (var (combo, _) in combos)
        {
            var idx = Indexes(lines, i => comboOf[i] is null && lines[i].ShopId == combo.ShopId && combo.ProductIds.Contains(lines[i].ProductId));
            if (idx.Sum(i => lines[i].Quantity) < combo.MinQuantity) continue;
            var value = idx.Sum(i => lines[i].LineTotal);
            var amount = combo.DiscountBp > 0 ? Money.Vnd(value).PercentBp(combo.DiscountBp).Value : Math.Min(combo.DiscountAmount, value);
            if (amount <= 0) continue;
            Spread(amount, idx, i => lines[i].LineTotal, comboDiscount);
            foreach (var i in idx) comboOf[i] = combo.PromotionId;
        }

        // 1) Shop vouchers, each on its own shop's covered lines (after combos)
        foreach (var shopId in shopIds)
        {
            if (!input.ShopVouchers.TryGetValue(shopId, out var voucher)) continue;
            var covered = Indexes(lines, i => lines[i].ShopId == shopId && voucher.Covers(lines[i]));
            var baseAmount = covered.Sum(i => lines[i].LineTotal - comboDiscount[i]);
            var outcome = Discount(voucher, covered.Count, baseAmount);
            outcomes.Add(outcome);
            if (!outcome.Applied || outcome.Discount == 0) continue;
            Spread(outcome.Discount, covered, i => lines[i].LineTotal - comboDiscount[i], shopDiscount);
            shopVoucherApplied[shopId] = voucher.Id;
        }

        // 2) Shipping and the platform free-shipping voucher (split across shops by their fee)
        var shippingFee = shopIds.ToDictionary(s => s, s => input.ShippingFees.GetValueOrDefault(s));
        var shippingDiscount = shopIds.ToDictionary(s => s, _ => 0L);
        if (input.FreeshipVoucher is { } freeship)
        {
            var covered = Indexes(lines, i => freeship.Covers(lines[i]));
            var goods = covered.Sum(i => lines[i].LineTotal - comboDiscount[i] - shopDiscount[i]);
            var shopsCovered = covered.Select(i => lines[i].ShopId).Distinct().Where(s => shippingFee[s] > 0).ToList();
            var totalShipping = shopsCovered.Sum(s => shippingFee[s]);
            string? problem = covered.Count == 0 ? "Không có sản phẩm phù hợp với mã này."
                : goods < freeship.MinOrder ? $"Mua thêm {Money.Vnd(freeship.MinOrder - goods)} để dùng mã này."
                : totalShipping == 0 ? "Đơn hàng không có phí vận chuyển để giảm." : null;
            var amount = problem is null ? Math.Min(totalShipping, freeship.MaxDiscount ?? totalShipping) : 0;
            outcomes.Add(new VoucherOutcome(freeship.Id, freeship.Code, amount, problem));
            if (amount > 0)
            {
                var parts = Money.Vnd(amount).Allocate(shopsCovered.Select(s => shippingFee[s]).ToList());
                for (var k = 0; k < shopsCovered.Count; k++) shippingDiscount[shopsCovered[k]] = parts[k].Value;
            }
        }

        // 3) Platform voucher on the value left after shop discounts
        long coinCashback = 0;
        if (input.PlatformVoucher is { } platform)
        {
            var covered = Indexes(lines, i => platform.Covers(lines[i]));
            var baseAmount = covered.Sum(i => lines[i].LineTotal - comboDiscount[i] - shopDiscount[i]);
            var outcome = Discount(platform, covered.Count, baseAmount);
            if (platform.Type == VoucherType.CoinCashback)
            {
                // Cash-back is paid in xu after the order completes: nothing comes off the price now
                coinCashback = outcome.Applied ? outcome.Discount : 0;
                outcomes.Add(outcome with { Discount = 0 });
            }
            else
            {
                outcomes.Add(outcome);
                if (outcome.Applied && outcome.Discount > 0)
                    Spread(outcome.Discount, covered, i => lines[i].LineTotal - comboDiscount[i] - shopDiscount[i], platformDiscount);
            }
        }

        // 4) Coins, capped at a share of the goods value after every discount
        var goodsAfter = Enumerable.Range(0, lines.Count).Select(i => lines[i].LineTotal - comboDiscount[i] - shopDiscount[i] - platformDiscount[i]).ToList();
        var coinMax = (long)((Int128)goodsAfter.Sum() * input.CoinMaxBp / 10_000);
        var coinUsed = Math.Max(0, Math.Min(input.CoinsAvailable, coinMax));
        if (coinUsed > 0) Spread(coinUsed, Enumerable.Range(0, lines.Count).ToList(), i => goodsAfter[i], coinDiscount);

        var shops = shopIds.Select(shopId =>
        {
            var idx = Indexes(lines, i => lines[i].ShopId == shopId);
            var priced = idx.Select(i => new PricedLine(lines[i], shopDiscount[i], platformDiscount[i], coinDiscount[i], comboDiscount[i], comboOf[i]))
                .ToList();
            return new PricedShop(shopId, priced,
                priced.Sum(p => p.Line.LineTotal), priced.Sum(p => p.ShopDiscount),
                shippingFee[shopId], shippingDiscount[shopId], priced.Sum(p => p.PlatformDiscount), priced.Sum(p => p.CoinDiscount),
                shopVoucherApplied.TryGetValue(shopId, out var vid) ? vid : null, priced.Sum(p => p.ComboDiscount));
        }).ToList();

        if (shops.Any(s => s.GrandTotal < 0)) throw new InvalidOperationException("PricingEngine produced a negative total.");
        return new PricingResult(shops, outcomes, coinUsed, coinMax, coinCashback);
    }

    /// <summary>What a voucher would take off <paramref name="baseAmount"/> (or why it does not apply).</summary>
    public static VoucherOutcome Discount(PricingVoucher voucher, int coveredLines, long baseAmount)
    {
        if (coveredLines == 0) return new VoucherOutcome(voucher.Id, voucher.Code, 0, "Không có sản phẩm phù hợp với mã này.");
        if (baseAmount < voucher.MinOrder)
            return new VoucherOutcome(voucher.Id, voucher.Code, 0, $"Mua thêm {Money.Vnd(voucher.MinOrder - baseAmount)} để dùng mã này.");
        var amount = voucher.Type switch
        {
            VoucherType.Amount => voucher.DiscountValue,
            VoucherType.Percent or VoucherType.CoinCashback =>
                Math.Min(Money.Vnd(baseAmount).PercentBp(voucher.DiscountPercentBp).Value, voucher.MaxDiscount ?? long.MaxValue),
            _ => 0,
        };
        return new VoucherOutcome(voucher.Id, voucher.Code, Math.Min(amount, baseAmount), null);
    }

    private static List<int> Indexes(List<PricingLine> lines, Func<int, bool> predicate) =>
        Enumerable.Range(0, lines.Count).Where(predicate).ToList();

    private static void Spread(long amount, List<int> indexes, Func<int, long> weight, long[] into)
    {
        var parts = Money.Vnd(amount).Allocate(indexes.Select(weight).ToList());
        for (var k = 0; k < indexes.Count; k++) into[indexes[k]] += parts[k].Value;
    }
}
