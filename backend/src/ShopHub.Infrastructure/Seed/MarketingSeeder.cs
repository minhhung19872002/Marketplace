using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Media;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Sample marketing data (spec section 7): a platform Flash Sale running now and the next one, the home banners (designed
/// artwork from Seed/Data/Art, the title is drawn by the page) and shortcuts, a popup and one campaign page. Side banners
/// carry no image: the buyer site draws them as voucher / freeship cards. Idempotent (only when empty).
/// </summary>
public sealed class MarketingSeeder(ShopHubDbContext db, IClock clock, IObjectStorage storage, IImageProcessor images, IServiceProvider services,
    ILogger<MarketingSeeder> logger)
{
    // Each title matches its artwork (hero-1 technology, hero-2 beauty, hero-3 home)
    public static readonly (string Title, string Link, string Art)[] MainBanners =
    [
        ("Công nghệ chính hãng — iPhone, AirPods, Apple Watch giá tốt", "/tim-kiem?q=apple", "hero-1.webp"),
        ("Mỹ phẩm & nước hoa chính hãng — ưu đãi đến 40%", "/danh-muc/sac-dep", "hero-2.webp"),
        ("Nhà đẹp mỗi ngày — đồ gia dụng, nội thất giá tốt", "/danh-muc/nha-cua-doi-song", "hero-3.webp"),
    ];

    public static readonly (string Title, string Link)[] SideBanners =
    [
        ("Mã giảm giá của sàn", "/su-kien/sieu-sale-10-10"),
        ("Freeship mọi đơn", "/tim-kiem?freeship=true"),
    ];

    // Spec II.1: Mã giảm giá, Freeship, Deal sốc, Mall… — each one opens a real page (E3); migration HomeShortcutsDeals adds the
    // first three to databases seeded before
    // Icon = an icon code the buyer site draws as SVG (or an image URL set by the admin)
    public static readonly (string Label, string Icon, string Link)[] Shortcuts =
    [
        ("Mã Giảm Giá", "voucher", "/tai-khoan/voucher"), ("Freeship", "freeship", "/tim-kiem?freeship=true"), ("Deal Sốc", "deal", "/flash-sale"),
        ("ShopHub Mall", "mall", "/tim-kiem?mall=true"), ("Shop Yêu Thích", "preferred", "/tim-kiem?preferred=true"),
        ("Hàng 4 Sao", "star", "/tim-kiem?minRating=4"), ("Bán Chạy", "hot", "/tim-kiem?sort=BestSelling"),
        ("Hàng Mới Về", "new", "/tim-kiem?sort=Newest"), ("Giá Từ Thấp", "price", "/tim-kiem?sort=PriceAsc"),
        ("Có Sẵn Hàng", "stock", "/tim-kiem?inStock=true"),
    ];

    /// <summary>
    /// The 10.10 frame (transparent PNG, uploaded as is — the image pipeline would flatten it) and the products taking
    /// part: Mall shops' products and about one in seven of the others, approved like an admin would.
    /// </summary>
    private async Task SeedCampaignFrameAsync(DateTimeOffset now, CancellationToken ct)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Slug == "sieu-sale-10-10", ct);
        if (campaign is null || campaign.FrameImageUrl is not null) return;
        await using var stream = typeof(MarketingSeeder).Assembly.GetManifestResourceStream("ShopHub.Infrastructure.Seed.Data.Art.frame-1010.png")
            ?? throw new InvalidOperationException("Thiếu tài nguyên Art/frame-1010.png.");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        await storage.PutAsync(Buckets.Banners, "seed/frame-1010.png", ms.ToArray(), "image/png", ct);
        campaign.SetFrame(storage.PublicUrl(Buckets.Banners, "seed/frame-1010.png"));
        var products = await (from p in db.Products.AsNoTracking()
                              join sh in db.Shops.AsNoTracking() on p.ShopId equals sh.Id
                              where p.Status == ProductStatus.Active
                              orderby p.Id
                              select new { p.Id, p.ShopId, Mall = sh.Type == Domain.Shops.ShopType.Mall }).ToListAsync(ct);
        var rng = new Random(1010);
        var picked = 0;
        foreach (var p in products.Where(p => p.Mall ? rng.Next(100) < 45 : rng.Next(100) < 15))
        {
            var registration = new CampaignRegistration(campaign.Id, p.ShopId, p.Id, now);
            registration.Decide(true, null, now);
            db.CampaignRegistrations.Add(registration);
            picked++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("SEED campaign frame: {Count} products take part", picked);
    }

    // Portrait slides at the left of the home "ShopHub Mall" block
    private async Task SeedMallBannersAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Banners.AnyAsync(b => b.Position == BannerPosition.Mall, ct)) return;
        string[] titles = ["ShopHub Mall — thương hiệu chính hãng", "ShopHub Mall — mỹ phẩm & nước hoa", "ShopHub Mall — thời trang chính hãng"];
        for (var i = 0; i < titles.Length; i++)
            db.Banners.Add(new Banner(BannerPosition.Mall, titles[i], await ArtAsync($"mall-{i + 1}.webp", ct), "/tim-kiem?mall=true", now.AddMinutes(-1),
                now.AddYears(1), i, null, now));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A few shop programmes running now (combo, add-on deal, gift) created through the seller command as the shop owner —
    /// so the "Combo giảm 10%", "Mua kèm deal sốc", "Có quà tặng" tags of the cards are real — and Freeship Xtra for the
    /// Mall shops and some others.
    /// </summary>
    private async Task SeedShopProgrammesAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Promotions.AnyAsync(p => p.Type != PromotionType.Discount, ct)) return;
        var shops = await db.Shops.Where(s => s.Status == Domain.Shops.ShopStatus.Active).OrderBy(s => s.Name).ToListAsync(ct);
        foreach (var shop in shops.Where((s, i) => s.Type == Domain.Shops.ShopType.Mall || i % 4 == 0))
            shop.SetXtra(Domain.Shops.XtraProgram.FreeshipXtra, true, now);
        await db.SaveChangesAsync(ct);

        var created = 0;
        for (var i = 0; i < shops.Count && created < 12; i++)
        {
            var shop = shops[i];
            var skus = await (from k in db.Skus.AsNoTracking()
                              join p in db.Products.AsNoTracking() on k.ProductId equals p.Id
                              where p.ShopId == shop.Id && p.Status == ProductStatus.Active && k.IsActive && k.Stock - k.Reserved > 10
                              orderby k.Price, k.Id
                              select new { SkuId = k.Id, k.ProductId, k.Price }).ToListAsync(ct);
            var products = skus.Select(k => k.ProductId).Distinct().ToList();
            if (products.Count < 5) continue;
            var type = (PromotionType)(1 + created % 3);  // Combo, AddOn, Gift
            var main = products.Skip(1).Take(4).ToList();
            var cheapest = skus.First();
            var input = type switch
            {
                PromotionType.Combo => new Application.Features.Marketing.PromotionInput(type, "Mua 2 giảm 10%", now.AddHours(-1), now.AddDays(30), main, null,
                    2, 1_000, 0, 0, 0, null, 0),
                PromotionType.AddOn => new Application.Features.Marketing.PromotionInput(type, "Mua kèm deal sốc", now.AddHours(-1), now.AddDays(30), main,
                    [new Application.Features.Marketing.PromotionSkuInput(cheapest.SkuId, Math.Max(1_000, cheapest.Price * 6 / 10 / 1_000 * 1_000), 2)],
                    0, 0, 0, 2, 0, null, 0),
                _ => new Application.Features.Marketing.PromotionInput(type, "Quà tặng cho đơn từ ₫300.000", now.AddHours(-1), now.AddDays(30), main, null,
                    0, 0, 0, 0, 300_000, cheapest.SkuId, 1),
            };
            try
            {
                await using var scope = services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ActingUser>().UserId = shop.OwnerId;
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Application.Features.Marketing.CreatePromotionCommand(shop.Id, input), ct);
                created++;
            }
            catch (Exception ex) when (ex is Application.Common.ConflictException or Domain.Common.BusinessRuleException or FluentValidation.ValidationException)
            {
                logger.LogWarning("Sample programme for {Shop} refused: {Message}", shop.Name, ex.Message);
            }
        }
        logger.LogInformation("SEED shop programmes: {Count} (combo / add-on / gift), Freeship Xtra for Mall shops and a few others", created);
    }

    /// <summary>A seed artwork (Seed/Data/Art) through the image pipeline into the banner bucket; its large public URL.</summary>
    private async Task<string> ArtAsync(string file, CancellationToken ct)
    {
        await using var stream = typeof(MarketingSeeder).Assembly.GetManifestResourceStream($"ShopHub.Infrastructure.Seed.Data.Art.{file}")
            ?? throw new InvalidOperationException($"Thiếu tài nguyên Art/{file}.");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var key = $"seed/{Path.GetFileNameWithoutExtension(file)}";
        foreach (var v in images.Process(ms.ToArray(), ImageSizes.Public).Variants)
            await storage.PutAsync(Buckets.Banners, ImageSizes.Key(key, v.MaxSide), v.WebP, "image/webp", ct);
        return storage.PublicUrl(Buckets.Banners, ImageSizes.Key(key, ImageSizes.Large));
    }

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
            // Running now: started a little before now, ends at the next 3-hour mark (Vietnam time), with sample buyers'
            // orders (OrderSampleSeeder). The slots after it are opened — and, in demo mode, filled — by FlashAutoOpener,
            // the hourly job that keeps the Flash Sale going after the seed (G4-C; replaces the seven days of G3, #191)
            var local = now.ToOffset(Vn);
            var end = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, Vn).AddHours(3 - local.Hour % 3).ToUniversalTime();
            var running = new FlashSaleSlot(FlashSaleOwner.Platform, null, now.AddMinutes(-10), end, 0, 0, [], now);
            db.FlashSaleSlots.Add(running);
            var n = 0;
            foreach (var p in products.Where(p => p.Sku is not null).Take(6))
            {
                n++;
                // Never more units than the stock can deliver; discounts from 10 % to 50 % (not one flat rate)
                var quota = Math.Min(20 + n * 5, p.Sku!.Available);
                var off = 10 + n * 17 % 41;
                var item = new FlashSaleItem(running.Id, p.Sku.Id, p.Id, p.ShopId, Math.Max(1_000, p.Sku.Price * (100 - off) / 100 / 1_000 * 1_000), quota, 2, now);
                item.Approve(now);
                db.FlashSaleItems.Add(item);
                db.PricePrograms.Add(new PriceProgram(item.SkuId, item.ShopId, PriceProgramKind.PlatformFlash, item.Id, item.FlashPrice, running.StartAt, running.EndAt));
            }
            await db.SaveChangesAsync(ct);
            await using var scope = services.CreateAsyncScope();
            var ahead = await scope.ServiceProvider.GetRequiredService<Application.Features.Marketing.FlashAutoOpener>().RunAsync(ct);
            logger.LogInformation("SEED flash sale: running slot with {Items} items; {Opened} slots opened ahead, {Filled} filled", n, ahead.Opened, ahead.Filled);
        }

        if (!await db.Banners.AnyAsync(ct))
        {
            var from = now.AddMinutes(-1);
            var to = now.AddYears(1);
            for (var i = 0; i < MainBanners.Length; i++)
                db.Banners.Add(new Banner(BannerPosition.HomeMain, MainBanners[i].Title, await ArtAsync(MainBanners[i].Art, ct), MainBanners[i].Link, from, to, i, null, now));
            for (var i = 0; i < SideBanners.Length; i++)
                db.Banners.Add(new Banner(BannerPosition.HomeSide, SideBanners[i].Title, i == 0 ? "voucher" : "freeship", SideBanners[i].Link, from, to, i, null, now));
            var shortcuts = Shortcuts;
            for (var i = 0; i < shortcuts.Length; i++)
                db.Banners.Add(new Banner(BannerPosition.Shortcut, shortcuts[i].Label, shortcuts[i].Icon, shortcuts[i].Link, from, to, i, null, now));
            db.Banners.Add(new Banner(BannerPosition.Popup, "Siêu sale 10.10 — mã giảm đến ₫100.000", await ArtAsync("popup.webp", ct), "/su-kien/sieu-sale-10-10", from, to, 0, null, now));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED banners: 3 main, 2 side, {Shortcuts} shortcuts, 1 popup", shortcuts.Length);
        }

        if (!await db.Campaigns.AnyAsync(ct))
        {
            // Fixed dates, like a real 10.10 sale: 08/10 – 12/10 (Vietnam time) of this year, or of next year once it is over
            // (G2-fix A3.12: the dates used to follow the seed day, "09/10 → 09/12")
            var vnNow = now.ToOffset(Vn);
            var year = vnNow > new DateTimeOffset(vnNow.Year, 10, 13, 0, 0, 0, Vn) ? vnNow.Year + 1 : vnNow.Year;
            var saleStart = new DateTimeOffset(year, 10, 8, 0, 0, 0, Vn).ToUniversalTime();
            var saleEnd = new DateTimeOffset(year, 10, 13, 0, 0, 0, Vn).ToUniversalTime();
            db.Campaigns.Add(new Campaign("Siêu Sale 10.10", "sieu-sale-10-10", saleStart, saleEnd,
            [
                new(CampaignBlockType.Banner, "Siêu Sale 10.10", await ArtAsync("event-1010.webp", ct), "/tim-kiem?sort=BestSelling", null, null, null, null, null),
                new(CampaignBlockType.Vouchers, "Mã giảm giá của sàn", null, null, ["SHOPHUB50", "FREESHIP", "SALE12"], null, null, null, null),
                new(CampaignBlockType.FlashSale, "Flash Sale đang diễn ra", null, null, null, null, null, null, null),
                new(CampaignBlockType.Products, "Deal dưới ₫200.000", null, "/tim-kiem?maxPrice=200000", null, null, null, 200_000, 12),
                // Shops put their products forward from Kênh Marketing → Chiến dịch của sàn; approved ones show here
                new(CampaignBlockType.Registered, "Sản phẩm của các shop tham gia", null, null, null, null, null, null, 24),
            ], now));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SEED campaign: sieu-sale-10-10");
        }

        await SeedCampaignFrameAsync(now, ct);
        await SeedMallBannersAsync(now, ct);
        await SeedShopProgrammesAsync(now, ct);
    }
}
