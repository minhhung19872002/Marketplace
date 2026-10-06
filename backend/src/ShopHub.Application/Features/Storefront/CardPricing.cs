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

    public async Task<PagedResult<ProductCardDto>> ApplyAsync(PagedResult<ProductCardDto> page, CancellationToken ct) =>
        page with { Items = await ApplyAsync(page.Items, ct) };
}
