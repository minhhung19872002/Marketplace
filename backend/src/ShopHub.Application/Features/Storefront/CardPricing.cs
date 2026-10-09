using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Marketing;
using ShopHub.Domain.Promo;

namespace ShopHub.Application.Features.Storefront;

/// <summary>
/// Puts the price in force right now on product cards (shop discount, shop / platform Flash Sale), from the same
/// <see cref="PriceBook"/> the product page and checkout use — a grid never shows a price the product page contradicts.
/// The search index keeps list prices (it cannot follow programmes that start and end by the minute), so every
/// handler that returns cards passes them through here.
/// </summary>
public sealed class CardPricing(IApplicationDbContext db, PriceBook prices, IClock clock)
{
    public async Task<IReadOnlyList<ProductCardDto>> ApplyAsync(IReadOnlyList<ProductCardDto> cards, CancellationToken ct)
    {
        if (cards.Count == 0) return cards;
        var ids = cards.Select(c => c.Id).Distinct().ToList();
        var now = clock.UtcNow;
        cards = await DecorateAsync(cards, ids, now, ct);
        // Only products with a programme in force need work; most pages have few or none
        var withPromo = await db.PricePrograms.AsNoTracking()
            .Where(p => p.IsActive && p.StartAt <= now && p.EndAt > now && db.Skus.Any(s => s.Id == p.SkuId && ids.Contains(s.ProductId)))
            .Select(p => p.SkuId).Distinct().CountAsync(ct);
        if (withPromo == 0) return cards;

        var skus = await db.Skus.AsNoTracking().Where(s => ids.Contains(s.ProductId) && s.IsActive)
            .Select(s => new { s.Id, s.ProductId, s.OriginalPrice }).ToListAsync(ct);
        var effective = await prices.ForSkusAsync(skus.Select(s => s.Id).ToList(), now, ct);
        var byProduct = skus.Where(s => effective.ContainsKey(s.Id)).GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(s => (Sku: s, Price: effective[s.Id])).ToList());

        return cards.Select(card =>
        {
            if (!byProduct.TryGetValue(card.Id, out var rows) || !rows.Any(r => r.Price.IsPromo)) return card;
            var cheapest = rows.OrderBy(r => r.Price.Price).ThenBy(r => r.Sku.Id).First();
            var min = cheapest.Price.Price;
            var max = rows.Max(r => r.Price.Price);
            var original = Math.Max(cheapest.Sku.OriginalPrice, cheapest.Price.BasePrice);
            var flash = rows.Any(r => r.Price.Kind is PriceProgramKind.PlatformFlash or PriceProgramKind.ShopFlash);
            return card with
            {
                MinPrice = min,
                MaxPrice = max,
                OriginalPrice = Math.Max(original, min),
                DiscountPercent = ProductCards.DiscountPercent(min, original),
                IsFlashSale = flash,
            };
        }).ToList();
    }

    /// <summary>
    /// Labels and campaign frame of the cards (G2-B5) — each one backed by data in force now: a running combo / add-on /
    /// gift programme that lists the product, the shop's Freeship Xtra, an approved registration in a running campaign
    /// that has a frame.
    /// </summary>
    private async Task<IReadOnlyList<ProductCardDto>> DecorateAsync(IReadOnlyList<ProductCardDto> cards, List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var promos = await (from pp in db.PromotionProducts.AsNoTracking()
                            join pr in db.Promotions.AsNoTracking() on pp.PromotionId equals pr.Id
                            where ids.Contains(pp.ProductId) && pr.Status == PromotionStatus.Active && pr.StartAt <= now && pr.EndAt > now
                            select new { pp.ProductId, pr.Type, pr.DiscountBp }).ToListAsync(ct);
        var shopIds = cards.Select(c => c.ShopId).Distinct().ToList();
        var xtra = (await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id) && s.FreeshipXtraSince != null).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        var frames = await (from r in db.CampaignRegistrations.AsNoTracking()
                            join c in db.Campaigns.AsNoTracking() on r.CampaignId equals c.Id
                            where ids.Contains(r.ProductId) && r.Status == CampaignRegistrationStatus.Approved && c.IsActive
                                  && c.StartAt <= now && c.EndAt > now && c.FrameImageUrl != null
                            orderby c.StartAt descending
                            select new { r.ProductId, c.FrameImageUrl, c.Name }).ToListAsync(ct);
        var frameOf = frames.GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.First());
        return cards.Select(card =>
        {
            var labels = new List<string>();
            foreach (var p in promos.Where(p => p.ProductId == card.Id).OrderBy(p => p.Type))
            {
                var label = p.Type switch
                {
                    PromotionType.Combo => p.DiscountBp > 0 ? $"Combo giảm {p.DiscountBp / 100}%" : "Mua combo giảm giá",
                    PromotionType.AddOn => "Mua kèm deal sốc",
                    PromotionType.Gift => "Có quà tặng",
                    _ => null,
                };
                if (label is not null && !labels.Contains(label)) labels.Add(label);
            }
            if (xtra.Contains(card.ShopId)) labels.Add("Freeship+");
            var frame = frameOf.GetValueOrDefault(card.Id);
            return card with { Labels = labels.Count > 0 ? labels.Take(2).ToList() : null, FrameUrl = frame?.FrameImageUrl, CampaignName = frame?.Name };
        }).ToList();
    }

    public async Task<PagedResult<ProductCardDto>> ApplyAsync(PagedResult<ProductCardDto> page, CancellationToken ct) =>
        page with { Items = await ApplyAsync(page.Items, ct) };
}
