using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Marketing;

/// <summary>
/// G4-C: platform Flash Sale slots open by themselves, so the home page never loses its Flash Sale after the seed's
/// slots run out. Every run opens the slots of FLASH.SLOT_HOURS for the next FLASH.AUTO_OPEN_DAYS days that are still
/// missing (no open platform slot overlapping that time) — never twice. In demo mode (SITE.MODE) an empty slot of the
/// window is also filled with six best-selling sample SKUs at 10–50 % off, approved and live like an approved shop
/// registration; in live mode slots stay empty for shops to register.
/// </summary>
public sealed class FlashAutoOpener(IApplicationDbContext db, ISystemParameters parameters, IClock clock, FlashSaleQuota quota,
    ILogger<FlashAutoOpener> logger)
{
    private const int ItemsPerSlot = 6;

    public record Result(int Opened, int Filled);

    public async Task<Result> RunAsync(CancellationToken ct)
    {
        var days = await parameters.GetIntAsync(ParameterKeys.FlashAutoOpenDays, ct);
        if (days <= 0) return new Result(0, 0);
        var hours = (await parameters.GetStringAsync(ParameterKeys.FlashSlotHours, ct))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => int.TryParse(h, out var v) ? v : -1).Where(h => h is >= 0 and <= 23).Distinct().Order().ToList();
        var length = (int)await parameters.GetIntAsync(ParameterKeys.FlashSlotLengthHours, ct);
        var minBp = (int)await parameters.GetIntAsync(ParameterKeys.FlashAutoMinDiscountBp, ct);
        var now = clock.UtcNow;
        var horizon = now.AddDays(days);
        var today = VietnamTime.Today(now);

        var opened = 0;
        for (var d = 0; d <= days; d++)
        {
            foreach (var hour in hours)
            {
                var start = new DateTimeOffset(today.AddDays(d).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(7)).ToUniversalTime();
                var end = start.AddHours(length);
                if (end <= now || start >= horizon) continue;
                if (await db.FlashSaleSlots.AnyAsync(s => s.Owner == FlashSaleOwner.Platform && s.Status == FlashSlotStatus.Open
                                                          && s.StartAt < end && s.EndAt > start, ct)) continue;
                db.FlashSaleSlots.Add(new FlashSaleSlot(FlashSaleOwner.Platform, null, start, end, minBp, 0, [], now));
                await db.SaveChangesAsync(ct);
                opened++;
            }
        }

        var filled = await SiteMode.IsDemoAsync(parameters, ct) ? await FillDemoAsync(now, horizon, ct) : 0;
        if (opened + filled > 0) logger.LogInformation("Flash Sale auto-open: {Opened} slots opened, {Filled} filled with sample items", opened, filled);
        return new Result(opened, filled);
    }

    /// <summary>Demo only: each open platform slot of the window without any item gets six sample SKUs.</summary>
    private async Task<int> FillDemoAsync(DateTimeOffset now, DateTimeOffset horizon, CancellationToken ct)
    {
        var empty = await db.FlashSaleSlots.Where(s => s.Owner == FlashSaleOwner.Platform && s.Status == FlashSlotStatus.Open
                                                       && s.EndAt > now && s.StartAt < horizon
                                                       && !db.FlashSaleItems.Any(i => i.SlotId == s.Id))
            .OrderBy(s => s.StartAt).ThenBy(s => s.Id).ToListAsync(ct);
        if (empty.Count == 0) return 0;
        // Best sellers of active shops with stock to spare, the cheapest SKU of each product
        var pool = await (from p in db.Products
                          join sh in db.Shops on p.ShopId equals sh.Id
                          where p.Status == ProductStatus.Active && sh.Status == ShopStatus.Active
                          orderby p.SoldCount descending, p.Id
                          select new
                          {
                              p.Id,
                              p.ShopId,
                              Sku = p.Skus.Where(s => s.IsActive && s.Stock - s.Reserved > 10).OrderBy(s => s.Price)
                                  .Select(s => new { s.Id, s.Price, Available = s.Stock - s.Reserved }).FirstOrDefault(),
                          }).Take(48).ToListAsync(ct);
        var candidates = pool.Where(p => p.Sku is not null).ToList();
        if (candidates.Count == 0) return 0;

        var items = new List<FlashSaleItem>();
        var filled = 0;
        foreach (var slot in empty)
        {
            // Rotate through the pool slot by slot so neighbouring slots show different products
            var hourIndex = slot.StartAt.ToUnixTimeSeconds() / 3600;
            var offset = (int)(hourIndex % candidates.Count);
            var taken = 0;
            for (var k = 0; k < candidates.Count && taken < ItemsPerSlot; k++)
            {
                var c = candidates[(offset + k) % candidates.Count];
                var sku = c.Sku!;
                // One SKU, one price programme at a time (exclusion constraint): skip a SKU already in a programme then
                if (await db.PricePrograms.AnyAsync(pp => pp.SkuId == sku.Id && pp.IsActive && pp.StartAt < slot.EndAt && pp.EndAt > slot.StartAt, ct))
                    continue;
                var off = 10 + (int)((hourIndex + k) * 17 % 41);
                var item = new FlashSaleItem(slot.Id, sku.Id, c.Id, c.ShopId, Math.Max(1_000, sku.Price * (100 - off) / 100 / 1_000 * 1_000),
                    Math.Min(20 + taken * 5, sku.Available), 2, now);
                item.Approve(now);
                db.FlashSaleItems.Add(item);
                FlashViews.GoLive(db, slot, item);
                // Saved one by one: the next SKU's overlap check must see this programme
                await db.SaveChangesAsync(ct);
                items.Add(item);
                taken++;
            }
            if (taken > 0) filled++;
        }
        foreach (var item in items) await quota.ReloadAsync(item.Id, ct);
        return filled;
    }
}
