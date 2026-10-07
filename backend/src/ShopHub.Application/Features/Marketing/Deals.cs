using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Checkout;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Marketing;

public record DealLine(Guid SkuId, Guid ProductId, Guid ShopId, int Quantity);

/// <summary>The unit price a cart line is sold at and why (programme price, add-on price, or the normal price).</summary>
public record LinePrice(long UnitPrice, long BasePrice, PriceProgramKind? Kind, Guid? RefId, FlashInfo? Flash, Guid? AddOnPromotionId,
    DiscountInfo? Discount = null);

public record GiftLine(Guid PromotionId, Guid ShopId, Guid SkuId, Guid ProductId, Guid CategoryId, string Name, string? Variant, string? ImageUrl,
    long OriginalPrice, int Quantity);

public record CheckoutDeals(IReadOnlyDictionary<Guid, LinePrice> Prices, IReadOnlyList<PricingCombo> Combos, IReadOnlyList<GiftLine> Gifts,
    IReadOnlyList<string> Problems);

/// <summary>
/// Everything a shop's marketing changes in a checkout (spec 3.6, 3.10): programme prices (discount / flash sale),
/// add-on deals (special price for add-on SKUs bought with a main product), combos (for the PricingEngine) and free
/// gifts (extra 0₫ lines). Read-only: quotas are only taken when the order is placed.
/// </summary>
public sealed class DealsBook(IApplicationDbContext db, PriceBook prices)
{
    public async Task<CheckoutDeals> ForCheckoutAsync(Guid userId, IReadOnlyList<DealLine> lines, DateTimeOffset at, CancellationToken ct)
    {
        var problems = new List<string>();
        var effective = await prices.ForSkusAsync(lines.Select(l => l.SkuId).ToList(), at, ct);
        var result = new Dictionary<Guid, LinePrice>();
        var names = await db.Products.AsNoTracking().IgnoreQueryFilters().Where(p => lines.Select(l => l.ProductId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        // Flash lines: the buyer's own limit and the units left, said now rather than at "Đặt hàng"
        var flashItems = effective.Values.Where(e => e.Flash is not null && e.IsPromo).Select(e => e.Flash!.ItemId).ToList();
        var mine = await db.FlashSaleBuyers.AsNoTracking().Where(b => b.UserId == userId && flashItems.Contains(b.ItemId))
            .ToDictionaryAsync(b => b.ItemId, b => b.Quantity, ct);
        // Discount lines with a per-buyer limit (L139): what this buyer already got at the discount price
        var limitedDiscounts = effective.Values.Where(e => e.Discount is { PerUserLimit: not null } && e.IsPromo).Select(e => e.Discount!.PromotionSkuId).ToList();
        var mineAtDiscount = await db.PromotionSkuBuyers.AsNoTracking().Where(b => b.UserId == userId && limitedDiscounts.Contains(b.PromotionSkuId))
            .ToDictionaryAsync(b => b.PromotionSkuId, b => b.Quantity, ct);
        foreach (var line in lines)
        {
            if (!effective.TryGetValue(line.SkuId, out var e)) continue;
            if (e.IsPromo && e.Flash is { } f)
            {
                var name = names.GetValueOrDefault(line.ProductId) ?? "Sản phẩm";
                if (mine.GetValueOrDefault(f.ItemId) + line.Quantity > f.PerUserLimit)
                    problems.Add($"Mỗi người chỉ mua tối đa {f.PerUserLimit} suất Flash Sale \"{name}\".");
                else if (line.Quantity > f.Left)
                    problems.Add($"\"{name}\" chỉ còn {f.Left} suất Flash Sale.");
            }
            if (e.IsPromo && e.Discount is { } d)
            {
                var name = names.GetValueOrDefault(line.ProductId) ?? "Sản phẩm";
                if (d.PerUserLimit is { } limit && mineAtDiscount.GetValueOrDefault(d.PromotionSkuId) + line.Quantity > limit)
                    problems.Add($"Mỗi người chỉ mua tối đa {limit} sản phẩm giá ưu đãi \"{name}\".");
                else if (d.Left is { } left && line.Quantity > left)
                    problems.Add($"\"{name}\" chỉ còn {left} suất giá ưu đãi.");
            }
            result[line.SkuId] = new LinePrice(e.Price, e.BasePrice, e.Kind, e.RefId, e.IsPromo ? e.Flash : null, null, e.IsPromo ? e.Discount : null);
        }

        var shopIds = lines.Select(l => l.ShopId).Distinct().ToList();
        var running = await db.Promotions.AsNoTracking().Include(p => p.Products).Include(p => p.Skus)
            .Where(p => shopIds.Contains(p.ShopId) && p.Status == PromotionStatus.Active && p.StartAt <= at && p.EndAt > at
                        && p.Type != PromotionType.Discount)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);

        // ----- add-on deals -----
        foreach (var deal in running.Where(p => p.Type == PromotionType.AddOn))
        {
            var mains = deal.Products.Select(x => x.ProductId).ToHashSet();
            if (!lines.Any(l => l.ShopId == deal.ShopId && mains.Contains(l.ProductId))) continue;
            var addOnLines = lines.Where(l => deal.Skus.Any(s => s.SkuId == l.SkuId) && !mains.Contains(l.ProductId)).ToList();
            if (addOnLines.Count == 0) continue;
            if (addOnLines.Sum(l => l.Quantity) > deal.MaxAddOnQuantity)
            {
                problems.Add($"\"{deal.Name}\": mỗi đơn mua kèm tối đa {deal.MaxAddOnQuantity} sản phẩm giá ưu đãi.");
                continue;
            }
            foreach (var line in addOnLines)
            {
                var addOn = deal.Skus.First(s => s.SkuId == line.SkuId).Price;
                var current = result[line.SkuId];
                // A flash / discount price that is already lower wins
                if (addOn < current.UnitPrice)
                    result[line.SkuId] = current with { UnitPrice = addOn, Kind = null, RefId = null, Flash = null, AddOnPromotionId = deal.Id, Discount = null };
            }
        }

        // ----- combos (applied by the PricingEngine) -----
        var combos = running.Where(p => p.Type == PromotionType.Combo)
            .Select(p => new PricingCombo(p.Id, p.ShopId, p.Products.Select(x => x.ProductId).ToList(), p.MinQuantity, p.DiscountBp, p.DiscountAmount))
            .ToList();

        // ----- free gifts -----
        var gifts = new List<GiftLine>();
        foreach (var deal in running.Where(p => p.Type == PromotionType.Gift && p.GiftSkuId is not null))
        {
            var mains = deal.Products.Select(x => x.ProductId).ToHashSet();
            var spend = lines.Where(l => l.ShopId == deal.ShopId && mains.Contains(l.ProductId)).Sum(l => result[l.SkuId].UnitPrice * l.Quantity);
            if (spend < deal.MinSpend) continue;
            var gift = await (from s in db.Skus.AsNoTracking()
                              join p in db.Products.AsNoTracking() on s.ProductId equals p.Id
                              where s.Id == deal.GiftSkuId && s.IsActive && p.Status == ProductStatus.Active && p.ShopId == deal.ShopId
                              select new { Sku = s, Product = p }).FirstOrDefaultAsync(ct);
            if (gift is null || gift.Sku.Available < deal.GiftQuantity) continue;  // gift ran out: the order goes on without it
            var optionIds = new[] { gift.Sku.Option1Id, gift.Sku.Option2Id }.Where(o => o is not null).Select(o => o!.Value).ToList();
            var values = await db.VariantOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Value, ct);
            var variant = string.Join(", ", optionIds.Select(id => values.GetValueOrDefault(id)).Where(v => !string.IsNullOrEmpty(v)));
            gifts.Add(new GiftLine(deal.Id, deal.ShopId, gift.Sku.Id, gift.Product.Id, gift.Product.CategoryId, gift.Product.Name,
                variant.Length == 0 ? null : variant, null, gift.Sku.Price, deal.GiftQuantity));
        }
        return new CheckoutDeals(result, combos, gifts, problems);
    }
}
