using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Sample marketing data (spec section 7): a platform Flash Sale running now and the next one, the home banners and
/// shortcuts that used to be hard-coded in the buyer site, a popup and one campaign page. Idempotent (only when empty).
/// </summary>
public sealed class MarketingSeeder(ShopHubDbContext db, IClock clock, ILogger<MarketingSeeder> logger)
{
    public static readonly (string Title, string Link)[] MainBanners =
    [
        ("Sinh Nhật ShopHub — hàng nghìn sản phẩm chính hãng giá tốt", "/su-kien/sieu-sale-10-10"),
        ("ShopHub Mall — thương hiệu chính hãng, đổi trả 15 ngày", "/tim-kiem?mall=true"),
        ("Công nghệ mới về — điện thoại, laptop, phụ kiện", "/tim-kiem?q=dien+thoai&sort=Newest"),
    ];

    public static readonly (string Title, string Link)[] SideBanners =
    [
        ("Mã giảm giá của sàn", "/su-kien/sieu-sale-10-10"),
        ("Freeship mọi đơn", "/tim-kiem?freeship=true"),
    ];

    // Spec II.1: Mã giảm giá, Freeship, Deal sốc, Mall… — each one opens a real page (E3); migration HomeShortcutsDeals adds the
    // first three to databases seeded before
    public static readonly (string Label, string Icon, string Link)[] Shortcuts =
    [
        ("Mã Giảm Giá", "🎟️", "/tai-khoan/voucher"), ("Freeship", "🚚", "/tim-kiem?freeship=true"), ("Deal Sốc", "⚡", "/flash-sale"),
        ("ShopHub Mall", "🏬", "/tim-kiem?mall=true"), ("Shop Yêu Thích", "💖", "/tim-kiem?preferred=true"),
        ("Hàng 4 Sao", "⭐", "/tim-kiem?minRating=4"), ("Bán Chạy", "🔥", "/tim-kiem?sort=BestSelling"),
        ("Hàng Mới Về", "🆕", "/tim-kiem?sort=Newest"), ("Giá Từ Thấp", "🏷️", "/tim-kiem?sort=PriceAsc"),
        ("Có Sẵn Hàng", "📦", "/tim-kiem?inStock=true"), ("Công Nghệ", "💻", "/danh-muc/may-tinh-laptop"),
        ("Sắc Đẹp", "💄", "/danh-muc/sac-dep"), ("Nhà Cửa", "🏠", "/danh-muc/nha-cua-doi-song"),
    ];

    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var products = await db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active)
            .OrderByDescending(p => p.SoldCount).ThenBy(p => p.Id).Take(16)
            .Select(p => new
            {
                p.Id, p.ShopId, p.Name,
                Image = p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                Sku = p.Skus.Where(s => s.IsActive && s.Stock - s.Reserved > 5).OrderBy(s => s.Price)
                    .Select(s => new { s.Id, s.Price, Available = s.Stock - s.Reserved }).FirstOrDefault(),
            }).ToListAsync(ct);
        if (products.Count == 0) return;

        if (!await db.FlashSaleSlots.AnyAsync(ct))
        {
            // Running: started a little before now, ends at the next 3-hour mark (Vietnam time); then the following slot
            var local = now.ToOffset(Vn);
            var end = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, Vn).AddHours(3 - local.Hour % 3).ToUniversalTime();
            var running = new FlashSaleSlot(FlashSaleOwner.Platform, null, now.AddMinutes(-10), end, 0, 0, [], now);
            var next = new FlashSaleSlot(FlashSaleOwner.Platform, null, end, end.AddHours(3), 2_000, 0, [], now);
            db.FlashSaleSlots.AddRange(running, next);
            var n = 0;
            foreach (var p in products.Where(p => p.Sku is not null).Take(12))
            {
                var slot = n++ % 2 == 0 ? running : next;
                // Never more units than the stock can deliver
                var quota = Math.Min(20 + n * 5, p.Sku!.Available);
                var item = new FlashSaleItem(slot.Id, p.Sku.Id, p.Id, p.ShopId, Math.Max(1_000, p.Sku.Price * 70 / 100 / 1_000 * 1_000), quota, 2, now);
                item.Approve(now);
                db.FlashSaleItems.Add(item);
                db.PricePrograms.Add(new PriceProgram(item.SkuId, item.ShopId, PriceProgramKind.PlatformFlash, item.Id, item.FlashPrice, slot.StartAt, slot.EndAt));
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED flash sale: 2 slots, {Items} items", n);
        }

        if (!await db.Banners.AnyAsync(ct))
        {
            var from = now.AddMinutes(-1);
            var to = now.AddYears(1);
            string ImageOf(int i) => products[i % products.Count].Image ?? "🛍️";
            for (var i = 0; i < MainBanners.Length; i++)
                db.Banners.Add(new Banner(BannerPosition.HomeMain, MainBanners[i].Title, ImageOf(i), MainBanners[i].Link, from, to, i, null, now));
            for (var i = 0; i < SideBanners.Length; i++)
                db.Banners.Add(new Banner(BannerPosition.HomeSide, SideBanners[i].Title, ImageOf(3 + i), SideBanners[i].Link, from, to, i, null, now));
            var shortcuts = Shortcuts;
            for (var i = 0; i < shortcuts.Length; i++)
                db.Banners.Add(new Banner(BannerPosition.Shortcut, shortcuts[i].Label, shortcuts[i].Icon, shortcuts[i].Link, from, to, i, null, now));
            db.Banners.Add(new Banner(BannerPosition.Popup, "Siêu sale 10.10 — mã giảm đến ₫100.000", ImageOf(5), "/su-kien/sieu-sale-10-10", from, to, 0, null, now));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED banners: 3 main, 2 side, {Shortcuts} shortcuts, 1 popup", shortcuts.Length);
        }

        if (!await db.Campaigns.AnyAsync(ct))
        {
            db.Campaigns.Add(new Campaign("Siêu Sale 10.10", "sieu-sale-10-10", now.AddMinutes(-1), now.AddMonths(2),
            [
                new(CampaignBlockType.Banner, "Siêu Sale 10.10", products[0].Image, "/tim-kiem?sort=BestSelling", null, null, null, null, null),
                new(CampaignBlockType.Vouchers, "Mã giảm giá của sàn", null, null, ["SHOPHUB50", "FREESHIP", "SALE12"], null, null, null, null),
                new(CampaignBlockType.FlashSale, "Flash Sale đang diễn ra", null, null, null, null, null, null, null),
                new(CampaignBlockType.Products, "Deal dưới ₫200.000", null, "/tim-kiem?maxPrice=200000", null, null, null, 200_000, 12),
                // Shops put their products forward from Kênh Marketing → Chiến dịch của sàn; approved ones show here
                new(CampaignBlockType.Registered, "Sản phẩm của các shop tham gia", null, null, null, null, null, null, 24),
            ], now));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED campaign: sieu-sale-10-10");
        }
    }
}
