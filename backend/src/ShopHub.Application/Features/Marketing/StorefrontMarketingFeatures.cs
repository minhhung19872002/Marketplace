using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Common;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Marketing;

// ---------- Flash Sale board (home page) ----------

public record FlashBoardItemDto(Guid ItemId, Guid ProductId, Guid SkuId, string Name, string? ImageUrl, long FlashPrice, long BasePrice, int DiscountPercent,
    int Quota, int Sold, int SoldPercent, int PerUserLimit);

public record FlashBoardSlotDto(Guid Id, DateTimeOffset StartAt, DateTimeOffset EndAt, bool Running);

/// <summary>Countdowns run on <see cref="ServerTime"/> (spec 3.10), never on the browser's clock.</summary>
public record FlashBoardDto(DateTimeOffset ServerTime, FlashBoardSlotDto? Slot, IReadOnlyList<FlashBoardSlotDto> Upcoming, IReadOnlyList<FlashBoardItemDto> Items);

public record FlashBoardQuery(Guid? SlotId = null) : IRequest<FlashBoardDto>;

public sealed class FlashBoardHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<FlashBoardQuery, FlashBoardDto>
{
    public async Task<FlashBoardDto> Handle(FlashBoardQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var slots = await db.FlashSaleSlots.AsNoTracking()
            .Where(s => s.Owner == FlashSaleOwner.Platform && s.Status == FlashSlotStatus.Open && s.EndAt > now && s.StartAt < now.AddDays(2))
            .OrderBy(s => s.StartAt).Take(10).ToListAsync(ct);
        var current = request.SlotId is { } id ? slots.FirstOrDefault(s => s.Id == id) : slots.FirstOrDefault(s => s.StartAt <= now) ?? slots.FirstOrDefault();
        if (current is null) return new FlashBoardDto(now, null, [], []);

        var rows = await (from i in db.FlashSaleItems.AsNoTracking()
                          join p in ProductCards.Visible(db).AsNoTracking() on i.ProductId equals p.Id
                          join s in db.Skus.AsNoTracking() on i.SkuId equals s.Id
                          where i.SlotId == current.Id && i.Status == FlashItemStatus.Approved && s.IsActive
                          orderby i.Sold * 1.0 / i.Quota descending, i.CreatedAt
                          select new
                          {
                              i.Id, i.ProductId, i.SkuId, p.Name, i.FlashPrice, BasePrice = s.Price, i.Quota, i.Sold, i.PerUserLimit,
                              Image = p.Media.Where(m => m.Type == Domain.Catalog.MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                          }).Take(60).ToListAsync(ct);
        var items = rows.Select(r => new FlashBoardItemDto(r.Id, r.ProductId, r.SkuId, r.Name, r.Image, r.FlashPrice, r.BasePrice,
            ProductCards.DiscountPercent(r.FlashPrice, r.BasePrice), r.Quota, r.Sold, (int)Math.Floor(r.Sold * 100.0 / r.Quota), r.PerUserLimit)).ToList();
        return new FlashBoardDto(now, new FlashBoardSlotDto(current.Id, current.StartAt, current.EndAt, current.StartAt <= now),
            slots.Where(s => s.Id != current.Id).Select(s => new FlashBoardSlotDto(s.Id, s.StartAt, s.EndAt, s.StartAt <= now)).ToList(), items);
    }
}

// ---------- deals of one product (product page) ----------

public record SkuDealDto(Guid SkuId, long Price, long BasePrice, string? Label, DateTimeOffset? EndsAt);

public record ProductFlashDto(Guid ItemId, DateTimeOffset StartAt, DateTimeOffset EndAt, int Quota, int Sold, int PerUserLimit, bool Platform);

/// <summary>
/// What an offer is about (L142): an add-on SKU at its deal price (added to the cart as is), a free gift (price 0), or a
/// product of the combo (no SKU: the buyer picks the variant on its page).
/// </summary>
public record OfferItemDto(Guid ProductId, Guid? SkuId, string Name, string? Variant, string? ImageUrl, long Price, long BasePrice);

public record ProductOfferDto(Guid PromotionId, PromotionType Type, string Name, string Text, IReadOnlyList<OfferItemDto>? Items = null);

public record ProductDealsDto(DateTimeOffset ServerTime, IReadOnlyList<SkuDealDto> Skus, ProductFlashDto? Flash, IReadOnlyList<ProductOfferDto> Offers);

public record ProductDealsQuery(Guid ProductId) : IRequest<ProductDealsDto>;

/// <summary>Programme prices of a product's SKUs, its running flash sale (with server time for the countdown) and shop offers.</summary>
public sealed class ProductDealsHandler(IApplicationDbContext db, PriceBook prices, IClock clock) : IRequestHandler<ProductDealsQuery, ProductDealsDto>
{
    public async Task<ProductDealsDto> Handle(ProductDealsQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var product = await db.Products.AsNoTracking().Where(p => p.Id == request.ProductId).Select(p => new { p.Id, p.ShopId }).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
        var skuIds = await db.Skus.AsNoTracking().Where(s => s.ProductId == product.Id && s.IsActive).Select(s => s.Id).ToListAsync(ct);
        var effective = await prices.ForSkusAsync(skuIds, now, ct);
        var skus = effective.Values.Select(e => new SkuDealDto(e.SkuId, e.Price, e.BasePrice,
            e.Kind switch { PriceProgramKind.Discount => "Giảm giá", null => null, _ => "Flash Sale" }, e.EndsAt)).ToList();

        var flash = effective.Values.Where(e => e.IsPromo && e.Flash is not null).Select(e => e.Flash!).FirstOrDefault();
        ProductFlashDto? flashDto = null;
        if (flash is not null)
        {
            var platform = await db.FlashSaleSlots.AsNoTracking().Where(s => s.Id == flash.SlotId).Select(s => s.Owner == FlashSaleOwner.Platform).FirstAsync(ct);
            // Quota of the whole product in this slot (every SKU's items)
            var all = await db.FlashSaleItems.AsNoTracking().Where(i => i.SlotId == flash.SlotId && i.ProductId == product.Id && i.Status == FlashItemStatus.Approved)
                .GroupBy(i => i.SlotId).Select(g => new { Quota = g.Sum(i => i.Quota), Sold = g.Sum(i => i.Sold) }).FirstAsync(ct);
            flashDto = new ProductFlashDto(flash.ItemId, flash.StartAt, flash.EndAt, all.Quota, all.Sold, flash.PerUserLimit, platform);
        }

        var offers = await db.Promotions.AsNoTracking().Include(p => p.Products).Include(p => p.Skus)
            .Where(p => p.ShopId == product.ShopId && p.Status == PromotionStatus.Active && p.StartAt <= now && p.EndAt > now
                        && p.Type != PromotionType.Discount && p.Products.Any(x => x.ProductId == product.Id))
            .OrderBy(p => p.EndAt).Take(5).ToListAsync(ct);

        // The SKUs and products the offers name, with a picture each
        var offerSkuIds = offers.SelectMany(p => p.Skus.Select(s => s.SkuId)).Concat(offers.Where(p => p.GiftSkuId is not null).Select(p => p.GiftSkuId!.Value))
            .Distinct().ToList();
        var offerSkus = await MarketingViews.SkusAsync(db, offerSkuIds, ct);
        var comboProducts = offers.Where(p => p.Type == PromotionType.Combo).SelectMany(p => p.Products.Select(x => x.ProductId)).ToList();
        var productIds = offerSkus.Values.Select(v => v.ProductId).Concat(comboProducts).Distinct().ToList();
        var cards = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id) && p.Status == Domain.Catalog.ProductStatus.Active)
            .Select(p => new { p.Id, p.Name, p.MinPrice, Image = p.Media.Where(m => m.Type == Domain.Catalog.MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault() })
            .ToDictionaryAsync(p => p.Id, ct);
        OfferItemDto? SkuItem(Guid skuId, long price) =>
            offerSkus.TryGetValue(skuId, out var k) && cards.TryGetValue(k.ProductId, out var c)
                ? new OfferItemDto(k.ProductId, skuId, k.Name, k.Variant, c.Image, price, k.Price) : null;
        IReadOnlyList<OfferItemDto> Items(Promotion p) => p.Type switch
        {
            PromotionType.AddOn => p.Skus.Select(s => SkuItem(s.SkuId, s.Price)).OfType<OfferItemDto>().ToList(),
            PromotionType.Gift when p.GiftSkuId is { } gift => SkuItem(gift, 0) is { } g ? [g] : [],
            PromotionType.Combo => p.Products.Where(x => cards.ContainsKey(x.ProductId))
                .Select(x => new OfferItemDto(x.ProductId, null, cards[x.ProductId].Name, null, cards[x.ProductId].Image, cards[x.ProductId].MinPrice, cards[x.ProductId].MinPrice))
                .ToList(),
            _ => [],
        };
        return new ProductDealsDto(now, skus, flashDto, offers.Select(p => new ProductOfferDto(p.Id, p.Type, p.Name, OfferText(p), Items(p))).ToList());
    }

    public static string OfferText(Promotion p) => p.Type switch
    {
        PromotionType.Combo => p.DiscountBp > 0 ? $"Mua {p.MinQuantity} sản phẩm giảm {p.DiscountBp / 100.0:0.#}%" : $"Mua {p.MinQuantity} sản phẩm giảm {Money.Vnd(p.DiscountAmount)}",
        PromotionType.AddOn => $"Mua kèm deal sốc (tối đa {p.MaxAddOnQuantity} sản phẩm)",
        PromotionType.Gift => $"Đơn từ {Money.Vnd(p.MinSpend)} được tặng quà",
        _ => p.Name,
    };
}

// ---------- shop page: programmes running now (II.5) ----------

// Type: Discount / Combo / AddOn / Gift (programmes) or FlashSale (the shop's own running flash sale)
public record ShopOfferDto(Guid Id, string Type, string Name, string Text, DateTimeOffset EndAt, int ProductCount);

public record ShopOffersQuery(Guid ShopId) : IRequest<IReadOnlyList<ShopOfferDto>>;

/// <summary>"Chương trình đang chạy" on the shop page: the shop's discounts, combos, add-on deals, gifts and own Flash Sale running now.</summary>
public sealed class ShopOffersHandler(IApplicationDbContext db, IClock clock) : IRequestHandler<ShopOffersQuery, IReadOnlyList<ShopOfferDto>>
{
    public async Task<IReadOnlyList<ShopOfferDto>> Handle(ShopOffersQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.Promotions.AsNoTracking().Include(p => p.Products)
            .Where(p => p.ShopId == request.ShopId && p.Status == PromotionStatus.Active && p.StartAt <= now && p.EndAt > now)
            .OrderBy(p => p.EndAt).ThenBy(p => p.Id).Take(10).ToListAsync(ct);
        var offers = rows.Select(p => new ShopOfferDto(p.Id, p.Type.ToString(), p.Name,
            p.Type == PromotionType.Discount ? $"Giảm giá đến hết {VietnamTime.ToLocal(p.EndAt):dd/MM}" : ProductDealsHandler.OfferText(p),
            p.EndAt, p.Products.Select(x => x.ProductId).Distinct().Count())).ToList();
        // E5: the shop's own flash sale running now, with the products it has on offer
        var flash = await db.FlashSaleSlots.AsNoTracking()
            .Where(s => s.Owner == FlashSaleOwner.Shop && s.ShopId == request.ShopId && s.Status == FlashSlotStatus.Open && s.StartAt <= now && s.EndAt > now)
            .OrderBy(s => s.EndAt).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id, s.EndAt,
                Products = db.FlashSaleItems.Where(i => i.SlotId == s.Id && i.Status == FlashItemStatus.Approved).Select(i => i.ProductId).Distinct().Count(),
            })
            .FirstOrDefaultAsync(ct);
        if (flash is { Products: > 0 })
            offers.Insert(0, new ShopOfferDto(flash.Id, "FlashSale", "Flash Sale của shop", $"Flash Sale của shop đến {VietnamTime.ToLocal(flash.EndAt):HH:mm}",
                flash.EndAt, flash.Products));
        return offers;
    }
}

// ---------- banners, shortcuts, popup ----------

public record PublicBannerDto(Guid Id, string Title, string ImageUrl, string Link);

public record HomeBannersDto(IReadOnlyList<PublicBannerDto> Main, IReadOnlyList<PublicBannerDto> Side, IReadOnlyList<PublicBannerDto> Shortcuts,
    PublicBannerDto? Popup, int PopupFrequencyHours, IReadOnlyList<string> PinnedKeywords, IReadOnlyList<PublicBannerDto>? Mall = null);

public record HomeBannersQuery : IRequest<HomeBannersDto>;

public sealed class HomeBannersHandler(IApplicationDbContext db, ISystemParameters parameters, IClock clock) : IRequestHandler<HomeBannersQuery, HomeBannersDto>
{
    public async Task<HomeBannersDto> Handle(HomeBannersQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.Banners.AsNoTracking().Where(b => b.IsActive && b.StartAt <= now && b.EndAt > now && b.Position != BannerPosition.Category)
            .OrderBy(b => b.SortOrder).ThenByDescending(b => b.StartAt).ToListAsync(ct);
        List<PublicBannerDto> Of(BannerPosition p) => rows.Where(b => b.Position == p).Select(b => new PublicBannerDto(b.Id, b.Title, b.ImageUrl, b.Link)).ToList();
        var pinned = (await parameters.GetStringAsync(ParameterKeys.SearchPinnedKeywords, ct))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(10).ToList();
        return new HomeBannersDto(Of(BannerPosition.HomeMain), Of(BannerPosition.HomeSide).Take(2).ToList(), Of(BannerPosition.Shortcut).Take(10).ToList(),
            Of(BannerPosition.Popup).FirstOrDefault(), (int)await parameters.GetIntAsync(ParameterKeys.PopupFrequencyHours, ct), pinned,
            Of(BannerPosition.Mall).Take(5).ToList());
    }
}

// ---------- campaign landing page (/su-kien/:slug) ----------

public record CampaignVoucherDto(Guid Id, string Code, string Name, VoucherType Type, long DiscountValue, int DiscountPercentBp, long? MaxDiscount,
    long MinOrder, DateTimeOffset EndAt);

public record CampaignBlockDto(CampaignBlockType Type, string? Title, string? ImageUrl, string? Link, IReadOnlyList<CampaignVoucherDto>? Vouchers,
    FlashBoardDto? FlashSale, IReadOnlyList<ProductCardDto>? Products);

public record CampaignPageDto(string Name, string Slug, DateTimeOffset StartAt, DateTimeOffset EndAt, DateTimeOffset ServerTime, IReadOnlyList<CampaignBlockDto> Blocks);

public record CampaignPageQuery(string Slug) : IRequest<CampaignPageDto>;

public sealed class CampaignPageHandler(IApplicationDbContext db, ISender sender, IClock clock, Storefront.CardPricing pricing)
    : IRequestHandler<CampaignPageQuery, CampaignPageDto>
{
    public async Task<CampaignPageDto> Handle(CampaignPageQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == request.Slug && c.IsActive && c.EndAt > now, ct)
                       ?? throw new NotFoundException("Chương trình không tồn tại hoặc đã kết thúc.");
        var parents = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);
        var blocks = new List<CampaignBlockDto>();
        foreach (var b in campaign.Blocks)
        {
            switch (b.Type)
            {
                case CampaignBlockType.Banner:
                    blocks.Add(new CampaignBlockDto(b.Type, b.Title, b.ImageUrl, b.Link, null, null, null));
                    break;
                case CampaignBlockType.Vouchers:
                    var codes = (b.VoucherCodes ?? []).Select(c => c.Trim().ToUpperInvariant()).ToList();
                    var vouchers = await db.Vouchers.AsNoTracking()
                        .Where(v => codes.Contains(v.Code) && v.IsActive && v.IsPublic && v.EndAt > now && v.Owner == VoucherOwner.Platform)
                        .Select(v => new CampaignVoucherDto(v.Id, v.Code, v.Name, v.Type, v.DiscountValue, v.DiscountPercentBp, v.MaxDiscount, v.MinOrder, v.EndAt))
                        .ToListAsync(ct);
                    blocks.Add(new CampaignBlockDto(b.Type, b.Title, null, null, vouchers, null, null));
                    break;
                case CampaignBlockType.FlashSale:
                    blocks.Add(new CampaignBlockDto(b.Type, b.Title, null, null, null, await sender.Send(new FlashBoardQuery(), ct), null));
                    break;
                case CampaignBlockType.Products:
                    var categories = b.CategoryId is { } root ? parents.Keys.Where(id => Under(id, root, parents)).ToList() : null;
                    var query = ProductCards.Visible(db).AsNoTracking();
                    if (categories is not null) query = query.Where(p => categories.Contains(p.CategoryId));
                    if (b.MaxPrice is { } max) query = query.Where(p => p.MinPrice <= max);
                    if (!string.IsNullOrWhiteSpace(b.Keyword))
                    {
                        var word = b.Keyword.Trim().ToLower();
                        query = query.Where(p => p.Name.ToLower().Contains(word));
                    }
                    var cards = await query.OrderByDescending(p => p.SoldCount).ThenBy(p => p.Id).Take(Math.Clamp(b.Limit ?? 12, 1, 48))
                        .Select(ProductCards.Row(db)).ToListAsync(ct);
                    blocks.Add(new CampaignBlockDto(b.Type, b.Title, null, b.Link, null, null, await pricing.ApplyAsync(cards.Select(ProductCards.ToDto).ToList(), ct)));
                    break;
                case CampaignBlockType.Registered:
                    // The shops' products the platform approved for this campaign (III.5), best sellers first
                    var approved = await ProductCards.Visible(db).AsNoTracking()
                        .Where(p => db.CampaignRegistrations.Any(r => r.CampaignId == campaign.Id && r.ProductId == p.Id && r.Status == CampaignRegistrationStatus.Approved))
                        .OrderByDescending(p => p.SoldCount).ThenBy(p => p.Id).Take(Math.Clamp(b.Limit ?? 24, 1, 48))
                        .Select(ProductCards.Row(db)).ToListAsync(ct);
                    blocks.Add(new CampaignBlockDto(b.Type, b.Title, null, b.Link, null, null, await pricing.ApplyAsync(approved.Select(ProductCards.ToDto).ToList(), ct)));
                    break;
            }
        }
        return new CampaignPageDto(campaign.Name, campaign.Slug, campaign.StartAt, campaign.EndAt, now, blocks);
    }

    private static bool Under(Guid id, Guid root, Dictionary<Guid, Guid?> parents)
    {
        for (Guid? c = id; c is not null; c = parents.GetValueOrDefault(c.Value))
            if (c == root) return true;
        return false;
    }
}
