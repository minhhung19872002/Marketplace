using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Marketing;

public record FlashInfo(Guid ItemId, Guid SlotId, int Quota, int Sold, int PerUserLimit, DateTimeOffset StartAt, DateTimeOffset EndAt)
{
    public int Left => Math.Max(0, Quota - Sold);
}

/// <summary>The price a SKU sells at right now and where it comes from (normal price when no programme runs).</summary>
/// <summary>A discount programme SKU with a per-buyer limit and / or a quota (L139).</summary>
public record DiscountInfo(Guid PromotionSkuId, int? Quota, int Sold, int? PerUserLimit)
{
    public int? Left => Quota is { } q ? Math.Max(0, q - Sold) : null;
}

public record EffectivePrice(Guid SkuId, long Price, long BasePrice, PriceProgramKind? Kind, Guid? RefId, FlashInfo? Flash, DateTimeOffset? EndsAt,
    DiscountInfo? Discount = null)
{
    public bool IsPromo => Kind is not null;
}

/// <summary>
/// Reads the price programmes in force (spec 3.10): shop discount, shop flash sale, platform flash sale (approved).
/// A flash item whose quota is used up no longer gives its price — the SKU sells at its normal price again.
/// </summary>
public sealed class PriceBook(IApplicationDbContext db)
{
    public async Task<Dictionary<Guid, EffectivePrice>> ForSkusAsync(IReadOnlyCollection<Guid> skuIds, DateTimeOffset at, CancellationToken ct)
    {
        if (skuIds.Count == 0) return [];
        var ids = skuIds.Distinct().ToList();
        var basePrices = await db.Skus.AsNoTracking().IgnoreQueryFilters().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Price, ct);
        var programs = await db.PricePrograms.AsNoTracking()
            .Where(p => p.IsActive && ids.Contains(p.SkuId) && p.StartAt <= at && p.EndAt > at).ToListAsync(ct);
        var flashIds = programs.Where(p => p.Kind != PriceProgramKind.Discount).Select(p => p.RefId).ToList();
        var flash = await (from i in db.FlashSaleItems.AsNoTracking()
                           join s in db.FlashSaleSlots.AsNoTracking() on i.SlotId equals s.Id
                           where flashIds.Contains(i.Id) && i.Status == FlashItemStatus.Approved && s.Status == FlashSlotStatus.Open
                           select new FlashInfo(i.Id, s.Id, i.Quota, i.Sold, i.PerUserLimit, s.StartAt, s.EndAt)).ToDictionaryAsync(f => f.ItemId, ct);

        var discountIds = programs.Where(p => p.Kind == PriceProgramKind.Discount).Select(p => p.RefId).ToList();
        var discounts = await db.PromotionSkus.AsNoTracking().Where(s => discountIds.Contains(s.PromotionId) && ids.Contains(s.SkuId))
            .ToDictionaryAsync(s => (s.PromotionId, s.SkuId), s => new DiscountInfo(s.Id, s.Quota, s.Sold, s.PerUserLimit), ct);

        var result = new Dictionary<Guid, EffectivePrice>();
        foreach (var skuId in ids)
        {
            if (!basePrices.TryGetValue(skuId, out var basePrice)) continue;
            var program = programs.FirstOrDefault(p => p.SkuId == skuId);
            if (program is null || program.Price >= basePrice)
            {
                result[skuId] = new EffectivePrice(skuId, basePrice, basePrice, null, null, null, null);
                continue;
            }
            if (program.Kind == PriceProgramKind.Discount)
            {
                var discount = discounts.GetValueOrDefault((program.RefId, skuId));
                // Every unit at the discount price is gone: the normal price applies again, as for a sold-out flash item
                result[skuId] = discount is { Left: 0 }
                    ? new EffectivePrice(skuId, basePrice, basePrice, null, null, null, null)
                    : new EffectivePrice(skuId, program.Price, basePrice, program.Kind, program.RefId, null, program.EndAt, discount);
                continue;
            }
            var info = flash.GetValueOrDefault(program.RefId);
            result[skuId] = info is { Left: > 0 }
                ? new EffectivePrice(skuId, program.Price, basePrice, program.Kind, program.RefId, info, program.EndAt)
                : new EffectivePrice(skuId, basePrice, basePrice, null, null, info, null);
        }
        return result;
    }
}

/// <summary>
/// Takes and gives back Flash Sale quota: Redis first (fast, atomic Lua), then the database row inside the order
/// transaction — <c>sold + n &lt;= quota</c> and the buyer's own counter <c>quantity + n &lt;= per_user_limit</c>.
/// The database decides; Redis only spares it the crowd.
/// </summary>
public sealed class FlashSaleQuota(IApplicationDbContext db, IFlashSaleCounter counter, IOutbox outbox, ILogger<FlashSaleQuota> logger)
{
    public record Taken(Guid ItemId, Guid UserId, int Quantity, bool InRedis);

    /// <summary>Must run inside the order transaction. On failure the caller gives back what was taken in Redis.</summary>
    public async Task<Taken> TakeAsync(FlashInfo flash, Guid userId, int quantity, string productName, CancellationToken ct)
    {
        var result = await counter.TryTakeAsync(flash.ItemId, userId, quantity, flash.PerUserLimit, ct);
        if (result == FlashTakeResult.NotLoaded)
        {
            await ReloadAsync(flash.ItemId, ct);
            result = await counter.TryTakeAsync(flash.ItemId, userId, quantity, flash.PerUserLimit, ct);
        }
        if (result == FlashTakeResult.SoldOut)
            throw new ConflictException($"\"{productName}\" đã hết suất Flash Sale.", "FLASH_SOLD_OUT");
        if (result == FlashTakeResult.OverUserLimit)
            throw new ConflictException($"Mỗi người chỉ mua tối đa {flash.PerUserLimit} suất Flash Sale \"{productName}\".", "FLASH_USER_LIMIT");
        if (result is FlashTakeResult.Unavailable or FlashTakeResult.NotLoaded)
            logger.LogWarning("Flash counter unavailable for item {ItemId}: database guard only", flash.ItemId);
        var taken = new Taken(flash.ItemId, userId, quantity, result == FlashTakeResult.Taken);
        try
        {
            var sold = await db.ExecuteSqlAsync($"""
                UPDATE promo.flash_sale_items SET sold = sold + {quantity}
                WHERE id = {flash.ItemId} AND status = 'Approved' AND sold + {quantity} <= quota
                """, ct);
            if (sold == 0) throw new ConflictException($"\"{productName}\" đã hết suất Flash Sale.", "FLASH_SOLD_OUT");
            var mine = await db.ExecuteSqlAsync($"""
                INSERT INTO promo.flash_sale_buyers (id, item_id, user_id, quantity) VALUES ({Guid.NewGuid()}, {flash.ItemId}, {userId}, {quantity})
                ON CONFLICT (item_id, user_id) DO UPDATE SET quantity = flash_sale_buyers.quantity + {quantity}
                WHERE flash_sale_buyers.quantity + {quantity} <= {flash.PerUserLimit}
                """, ct);
            if (mine == 0 || quantity > flash.PerUserLimit)
                throw new ConflictException($"Mỗi người chỉ mua tối đa {flash.PerUserLimit} suất Flash Sale \"{productName}\".", "FLASH_USER_LIMIT");
            // The last unit: the SKU sells at its normal price from now on — the search index follows (same transaction)
            if (await db.FlashSaleItems.AnyAsync(i => i.Id == flash.ItemId && i.Sold >= i.Quota, ct))
                outbox.Enqueue(OutboxTypes.SearchSyncSkus, new SearchSyncSkusPayload([await SkuOfAsync(flash.ItemId, ct)]));
        }
        catch
        {
            await GiveBackRedisAsync([taken], ct);
            throw;
        }
        return taken;
    }

    /// <summary>
    /// Cheap gate before the (heavy) checkout is built: when Redis already gave every unit of a flash item in the
    /// buyer's selection away while the database has not caught up yet, the buyer cannot get it at the flash price —
    /// say so at once instead of pricing the whole checkout for nothing. Once the database shows the item sold out,
    /// the normal price applies and the checkout goes on as usual.
    /// </summary>
    public async Task RefuseIfSoldOutAsync(Guid userId, DateTimeOffset at, CancellationToken ct)
    {
        var skuIds = await db.CartItems.AsNoTracking().Where(i => i.IsSelected && db.Carts.Any(c => c.Id == i.CartId && c.UserId == userId))
            .Select(i => i.SkuId).ToListAsync(ct);
        if (skuIds.Count == 0) return;
        var items = await (from p in db.PricePrograms.AsNoTracking()
                           join i in db.FlashSaleItems.AsNoTracking() on p.RefId equals i.Id
                           where p.IsActive && p.Kind != PriceProgramKind.Discount && skuIds.Contains(p.SkuId) && p.StartAt <= at && p.EndAt > at
                                 && i.Sold < i.Quota
                           select i.Id).ToListAsync(ct);
        foreach (var itemId in items)
            if (await counter.LeftAsync(itemId, ct) is <= 0)
                throw new ConflictException("Sản phẩm Flash Sale vừa hết suất.", "FLASH_SOLD_OUT");
    }

    public async Task GiveBackRedisAsync(IEnumerable<Taken> taken, CancellationToken ct)
    {
        foreach (var t in taken.Where(t => t.InRedis))
        {
            try
            {
                await counter.GiveBackAsync(t.ItemId, t.UserId, t.Quantity, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The reconcile job reloads the counter from the database anyway
                logger.LogWarning(ex, "Could not give back flash quota of item {ItemId} in Redis", t.ItemId);
            }
        }
    }

    /// <summary>An order with flash lines was cancelled / its checkout expired: the units go back (database, then Redis).</summary>
    public async Task ReleaseAsync(Guid userId, IEnumerable<(Guid ItemId, int Quantity)> lines, CancellationToken ct)
    {
        foreach (var (itemId, quantity) in lines)
        {
            await db.ExecuteSqlAsync($"UPDATE promo.flash_sale_items SET sold = sold - {quantity} WHERE id = {itemId} AND sold >= {quantity}", ct);
            await db.ExecuteSqlAsync($"""
                UPDATE promo.flash_sale_buyers SET quantity = quantity - {quantity}
                WHERE item_id = {itemId} AND user_id = {userId} AND quantity >= {quantity}
                """, ct);
            await GiveBackRedisAsync([new Taken(itemId, userId, quantity, true)], ct);
            // Units back on a sold-out item bring the flash price back
            outbox.Enqueue(OutboxTypes.SearchSyncSkus, new SearchSyncSkusPayload([await SkuOfAsync(itemId, ct)]));
        }
    }

    private Task<Guid> SkuOfAsync(Guid itemId, CancellationToken ct) =>
        db.FlashSaleItems.AsNoTracking().Where(i => i.Id == itemId).Select(i => i.SkuId).FirstAsync(ct);

    public async Task ReloadAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.FlashSaleItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return;
        var buyers = await db.FlashSaleBuyers.AsNoTracking().Where(b => b.ItemId == itemId && b.Quantity > 0).ToDictionaryAsync(b => b.UserId, b => b.Quantity, ct);
        await counter.LoadAsync(itemId, Math.Max(0, item.Quota - item.Sold), buyers, ct);
    }
}

/// <summary>
/// Units of a discount programme SKU (L139): <c>sold + n &lt;= quota</c> and the buyer's own counter
/// <c>quantity + n &lt;= per_user_limit</c>, both conditional UPDATEs inside the order transaction (spec 6.2); a
/// cancelled order gives its units back.
/// </summary>
public sealed class DiscountQuota(IApplicationDbContext db, IOutbox outbox)
{
    /// <summary>Must run inside the order transaction.</summary>
    public async Task TakeAsync(DiscountInfo discount, Guid userId, int quantity, string productName, CancellationToken ct)
    {
        var sold = await db.ExecuteSqlAsync($"""
            UPDATE promo.promotion_skus SET sold = sold + {quantity}
            WHERE id = {discount.PromotionSkuId} AND (quota IS NULL OR sold + {quantity} <= quota)
            """, ct);
        if (sold == 0) throw new ConflictException($"\"{productName}\" đã hết suất giá ưu đãi.", "DISCOUNT_SOLD_OUT");
        if (discount.PerUserLimit is { } limit)
        {
            var mine = await db.ExecuteSqlAsync($"""
                INSERT INTO promo.promotion_sku_buyers (id, promotion_sku_id, user_id, quantity) VALUES ({Guid.NewGuid()}, {discount.PromotionSkuId}, {userId}, {quantity})
                ON CONFLICT (promotion_sku_id, user_id) DO UPDATE SET quantity = promotion_sku_buyers.quantity + {quantity}
                WHERE promotion_sku_buyers.quantity + {quantity} <= {limit}
                """, ct);
            if (mine == 0 || quantity > limit)
                throw new ConflictException($"Mỗi người chỉ mua tối đa {limit} sản phẩm giá ưu đãi \"{productName}\".", "DISCOUNT_USER_LIMIT");
        }
        // The last unit: the SKU sells at its normal price from now on — the search index follows (same transaction)
        if (discount.Quota is { } quota && await db.PromotionSkus.AnyAsync(s => s.Id == discount.PromotionSkuId && s.Sold >= quota, ct))
            outbox.Enqueue(OutboxTypes.SearchSyncSkus, new SearchSyncSkusPayload([await SkuOfAsync(discount.PromotionSkuId, ct)]));
    }

    /// <summary>An order with discount lines was cancelled / its checkout expired: the units go back.</summary>
    public async Task ReleaseAsync(Guid userId, IEnumerable<(Guid PromotionId, Guid SkuId, int Quantity)> lines, CancellationToken ct)
    {
        foreach (var (promotionId, skuId, quantity) in lines)
        {
            var row = await db.PromotionSkus.AsNoTracking().Where(s => s.PromotionId == promotionId && s.SkuId == skuId)
                .Select(s => new { s.Id, s.Quota }).FirstOrDefaultAsync(ct);
            if (row is null) continue;
            await db.ExecuteSqlAsync($"UPDATE promo.promotion_skus SET sold = sold - {quantity} WHERE id = {row.Id} AND sold >= {quantity}", ct);
            await db.ExecuteSqlAsync($"""
                UPDATE promo.promotion_sku_buyers SET quantity = quantity - {quantity}
                WHERE promotion_sku_id = {row.Id} AND user_id = {userId} AND quantity >= {quantity}
                """, ct);
            // Units back on a sold-out SKU bring the discount price back
            if (row.Quota is not null) outbox.Enqueue(OutboxTypes.SearchSyncSkus, new SearchSyncSkusPayload([skuId]));
        }
    }

    private Task<Guid> SkuOfAsync(Guid promotionSkuId, CancellationToken ct) =>
        db.PromotionSkus.AsNoTracking().Where(s => s.Id == promotionSkuId).Select(s => s.SkuId).FirstAsync(ct);
}

/// <summary>Flash counters follow the database (spec 3.10 "đối chiếu"): items of slots running now or starting within the hour.</summary>
public sealed class FlashSaleReconciler(IApplicationDbContext db, FlashSaleQuota quota, IClock clock, ILogger<FlashSaleReconciler> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ids = await (from i in db.FlashSaleItems.AsNoTracking()
                         join s in db.FlashSaleSlots.AsNoTracking() on i.SlotId equals s.Id
                         where i.Status == FlashItemStatus.Approved && s.Status == FlashSlotStatus.Open && s.EndAt > now && s.StartAt <= now.AddHours(1)
                         select i.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await quota.ReloadAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not reload flash counter {ItemId}", id);
            }
        }
        return ids.Count;
    }
}
