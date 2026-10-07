using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Chat;

/// <summary>
/// Time-based notifications of spec VII (job <c>engage.reminders</c>): an order that completes itself within a day,
/// a saved voucher that expires within a day and is still unused, a product on the wishlist that is now on sale (a
/// discount or flash price started in the last day) or back in stock. Each one at most once (dedupe key). The wishlist
/// news are promotions — at most once a day per person, broadcasts included (<see cref="PromoNotifications"/>), and
/// not at all for whoever turned promotions off; one held back is sent on a later run while still true. The voucher
/// reminder is about the buyer's own voucher wallet: tab "Ví & xu", outside the promotion quota.
/// </summary>
public sealed class ReminderService(IApplicationDbContext db, Admin.MessageTemplates templates, IClock clock, ILogger<ReminderService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var created = 0;

        var completing = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Delivered && o.AutoCompleteAt != null && o.AutoCompleteAt > now && o.AutoCompleteAt <= now.AddDays(1))
            .Select(o => new { o.Id, o.BuyerId, o.Code }).Take(2_000).ToListAsync(ct);
        foreach (var o in completing)
            created += await AddAsync(o.BuyerId, NotificationCategory.Order, Admin.TemplateCatalog.ReminderAutoComplete, new() { ["code"] = o.Code },
                $"/tai-khoan/don-mua/{o.Code}", "order", o.Id, $"auto-complete:{o.Id}", now, ct);

        var expiring = await (from c in db.VoucherClaims.AsNoTracking()
                              join v in db.Vouchers.AsNoTracking() on c.VoucherId equals v.Id
                              where v.IsActive && v.EndAt > now && v.EndAt <= now.AddDays(1)
                                    && !db.VoucherUserCounters.Any(u => u.VoucherId == v.Id && u.UserId == c.UserId && u.UsedCount > 0)
                              select new { c.UserId, v.Id, v.Code }).Take(5_000).ToListAsync(ct);
        foreach (var v in expiring)
            created += await AddAsync(v.UserId, NotificationCategory.Wallet, Admin.TemplateCatalog.ReminderVoucherExpiring, new() { ["code"] = v.Code },
                "/tai-khoan/voucher", "voucher", v.Id, $"voucher-expiring:{v.Id}", now, ct);

        var onSale = await (from w in db.Wishlists.AsNoTracking()
                            join s in db.Skus.AsNoTracking() on w.ProductId equals s.ProductId
                            join p in db.PricePrograms.AsNoTracking() on s.Id equals p.SkuId
                            join pr in db.Products.AsNoTracking() on w.ProductId equals pr.Id
                            where p.IsActive && p.StartAt <= now && p.StartAt > now.AddDays(-1) && p.EndAt > now
                            select new { w.UserId, w.ProductId, pr.Name, ProgramId = p.Id }).Take(5_000).ToListAsync(ct);
        var promos = new List<PromoNotice>();
        foreach (var w in onSale.DistinctBy(x => (x.UserId, x.ProductId)))
        {
            var (title, body) = await templates.NoticeAsync(Admin.TemplateCatalog.ReminderWishlistSale, new Dictionary<string, string> { ["product"] = w.Name }, ct);
            promos.Add(new PromoNotice(w.UserId, title, body, $"/san-pham/{w.ProductId}", "product", w.ProductId, $"wishlist-sale:{w.ProductId}:{w.ProgramId}"));
        }

        // Có hàng lại: remember wishlisted products seen sold out; once one is buyable again, tell (and forget)
        var watched = await (from w in db.Wishlists
                             join pr in db.Products on w.ProductId equals pr.Id
                             where pr.Status == Domain.Catalog.ProductStatus.Active
                             select new { Wish = w, pr.Name, InStock = pr.Skus.Any(s => s.IsActive && s.Stock - s.Reserved > 0) })
            .Where(x => !x.InStock || x.Wish.SoldOutSeenAt != null).Take(10_000).ToListAsync(ct);
        var restocked = new List<(Wishlist Wish, string Dedupe)>();
        foreach (var x in watched)
        {
            if (!x.InStock)
            {
                x.Wish.SeenSoldOut(now);
                continue;
            }
            var dedupe = $"wishlist-restock:{x.Wish.ProductId}:{x.Wish.SoldOutSeenAt!.Value.ToUnixTimeSeconds()}";
            restocked.Add((x.Wish, dedupe));
            var (title, body) = await templates.NoticeAsync(Admin.TemplateCatalog.ReminderWishlistRestock, new Dictionary<string, string> { ["product"] = x.Name }, ct);
            promos.Add(new PromoNotice(x.Wish.UserId, title, body, $"/san-pham/{x.Wish.ProductId}", "product", x.Wish.ProductId, dedupe));
        }

        await db.SaveChangesAsync(ct);
        // Sale news first (they are older), one a day per person: the rest waits for a later run
        created += await PromoNotifications.SendAsync(db, promos, now, ct);
        if (restocked.Count > 0)
        {
            // Told (now or before): forget the sold-out mark; held back by the daily quota: try again next run
            var users = restocked.Select(r => r.Wish.UserId).Distinct().ToList();
            var keys = restocked.Select(r => r.Dedupe).ToList();
            var told = (await db.Notifications.AsNoTracking().Where(n => users.Contains(n.UserId) && keys.Contains(n.DedupeKey!))
                .Select(n => new { n.UserId, n.DedupeKey }).ToListAsync(ct)).Select(n => (n.UserId, n.DedupeKey!)).ToHashSet();
            foreach (var (wish, dedupe) in restocked.Where(r => told.Contains((r.Wish.UserId, r.Dedupe)))) wish.BackInStock();
            await db.SaveChangesAsync(ct);
        }
        if (created > 0) logger.LogInformation("Reminders: {Count} notifications", created);
        return created;
    }

    private readonly HashSet<(Guid, string)> _seen = [];

    /// <summary>One reminder from its template (F7), once per dedupe key.</summary>
    private async Task<int> AddAsync(Guid userId, NotificationCategory category, string templateKey, Dictionary<string, string> values, string link,
        string refType, Guid refId, string dedupe, DateTimeOffset now, CancellationToken ct)
    {
        // Promotions go through PromoNotifications (daily quota, opt-out); this path is for reminders only
        if (category == NotificationCategory.Promotion) throw new InvalidOperationException("Promotion notifications go through PromoNotifications.");
        if (_seen.Contains((userId, dedupe)) || db.Notifications.Any(n => n.UserId == userId && n.DedupeKey == dedupe)) return 0;
        _seen.Add((userId, dedupe));
        var (title, body) = await templates.NoticeAsync(templateKey, values, ct);
        db.Notifications.Add(new Notification(userId, category, title, body, link, refType, refId, now, dedupe));
        return 1;
    }
}
