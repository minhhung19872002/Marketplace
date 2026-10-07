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
/// discount or flash price started in the last day) or back in stock. Each one at most once (dedupe key); promotion
/// notifications at most once a day per person (spec VII) — one held back is sent on a later run while still true.
/// </summary>
public sealed class ReminderService(IApplicationDbContext db, IClock clock, ILogger<ReminderService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var created = 0;

        var completing = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Delivered && o.AutoCompleteAt != null && o.AutoCompleteAt > now && o.AutoCompleteAt <= now.AddDays(1))
            .Select(o => new { o.Id, o.BuyerId, o.Code }).Take(2_000).ToListAsync(ct);
        foreach (var o in completing)
            created += Add(o.BuyerId, NotificationCategory.Order, "Đơn hàng sắp tự hoàn thành",
                $"Đơn {o.Code} sẽ tự hoàn thành trong 24 giờ. Nếu có vấn đề, hãy yêu cầu trả hàng trước thời điểm này.", $"/tai-khoan/don-mua/{o.Code}",
                "order", o.Id, $"auto-complete:{o.Id}", now);

        var expiring = await (from c in db.VoucherClaims.AsNoTracking()
                              join v in db.Vouchers.AsNoTracking() on c.VoucherId equals v.Id
                              where v.IsActive && v.EndAt > now && v.EndAt <= now.AddDays(1)
                                    && !db.VoucherUserCounters.Any(u => u.VoucherId == v.Id && u.UserId == c.UserId && u.UsedCount > 0)
                              select new { c.UserId, v.Id, v.Code }).Take(5_000).ToListAsync(ct);
        foreach (var v in expiring)
            created += Add(v.UserId, NotificationCategory.Promotion, "Voucher sắp hết hạn", $"Mã {v.Code} trong ví của bạn sẽ hết hạn trong 24 giờ.",
                "/tai-khoan/voucher", "voucher", v.Id, $"voucher-expiring:{v.Id}", now);

        var onSale = await (from w in db.Wishlists.AsNoTracking()
                            join s in db.Skus.AsNoTracking() on w.ProductId equals s.ProductId
                            join p in db.PricePrograms.AsNoTracking() on s.Id equals p.SkuId
                            join pr in db.Products.AsNoTracking() on w.ProductId equals pr.Id
                            where p.IsActive && p.StartAt <= now && p.StartAt > now.AddDays(-1) && p.EndAt > now
                            select new { w.UserId, w.ProductId, pr.Name, ProgramId = p.Id }).Take(5_000).ToListAsync(ct);
        foreach (var w in onSale.DistinctBy(x => (x.UserId, x.ProductId)))
            created += Add(w.UserId, NotificationCategory.Promotion, "Sản phẩm yêu thích đang giảm giá", $"\"{w.Name}\" bạn đã thích đang có giá ưu đãi.",
                $"/san-pham/{w.ProductId}", "product", w.ProductId, $"wishlist-sale:{w.ProductId}:{w.ProgramId}", now);

        // Có hàng lại: remember wishlisted products seen sold out; once one is buyable again, tell (and forget)
        var watched = await (from w in db.Wishlists
                             join pr in db.Products on w.ProductId equals pr.Id
                             where pr.Status == Domain.Catalog.ProductStatus.Active
                             select new { Wish = w, pr.Name, InStock = pr.Skus.Any(s => s.IsActive && s.Stock - s.Reserved > 0) })
            .Where(x => !x.InStock || x.Wish.SoldOutSeenAt != null).Take(10_000).ToListAsync(ct);
        foreach (var x in watched)
        {
            if (!x.InStock)
            {
                x.Wish.SeenSoldOut(now);
                continue;
            }
            var seen = x.Wish.SoldOutSeenAt!.Value;
            if (Add(x.Wish.UserId, NotificationCategory.Promotion, "Sản phẩm yêu thích đã có hàng lại", $"\"{x.Name}\" bạn đã thích đã có hàng trở lại.",
                    $"/san-pham/{x.Wish.ProductId}", "product", x.Wish.ProductId, $"wishlist-restock:{x.Wish.ProductId}:{seen.ToUnixTimeSeconds()}", now) > 0
                || db.Notifications.Any(n => n.UserId == x.Wish.UserId && n.DedupeKey == $"wishlist-restock:{x.Wish.ProductId}:{seen.ToUnixTimeSeconds()}"))
            {
                x.Wish.BackInStock();
                created++;
            }
        }

        if (created > 0 || db.Wishlists.Local.Any())
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Reminders: {Count} notifications", created);
        }
        return created;
    }

    private readonly HashSet<(Guid, string)> _seen = [];
    private readonly HashSet<Guid> _promotedToday = [];

    private int Add(Guid userId, NotificationCategory category, string title, string body, string link, string refType, Guid refId, string dedupe,
        DateTimeOffset now)
    {
        if (_seen.Contains((userId, dedupe)) || db.Notifications.Any(n => n.UserId == userId && n.DedupeKey == dedupe)) return 0;
        if (category == NotificationCategory.Promotion)
        {
            // At most one promotion a day (Vietnam day) per person, broadcasts included
            var dayStart = new DateTimeOffset(Common.VietnamTime.ToLocal(now).Date, TimeSpan.FromHours(7)).ToUniversalTime();
            if (_promotedToday.Contains(userId)
                || db.Notifications.Any(n => n.UserId == userId && n.Category == NotificationCategory.Promotion && n.CreatedAt >= dayStart)) return 0;
            _promotedToday.Add(userId);
        }
        _seen.Add((userId, dedupe));
        db.Notifications.Add(new Notification(userId, category, title, body, link, refType, refId, now, dedupe));
        return 1;
    }
}
