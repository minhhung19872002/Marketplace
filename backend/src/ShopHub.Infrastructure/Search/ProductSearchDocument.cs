using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Search;

/// <summary>What Meilisearch stores per buyable product. Text fields also exist accent-folded ("…Folded").</summary>
public sealed record ProductSearchDocument(
    string Id,
    string Name,
    string NameFolded,
    string Slug,
    string? ImageUrl,
    long MinPrice,
    long MaxPrice,
    long OriginalPrice,
    int DiscountPercent,
    double RatingAvg,
    int RatingFloor,
    int RatingCount,
    int SoldCount,
    bool InStock,
    string ShopId,
    string ShopName,
    string ShopNameFolded,
    bool IsMall,
    bool IsPreferred,
    string? ProvinceCode,
    string? ProvinceName,
    string CategoryId,
    IReadOnlyList<string> CategoryIds,
    string CategoryNamesFolded,
    string? BrandId,
    string? BrandName,
    string Condition,
    IReadOnlyList<string> Attributes,
    long PublishedAt,
    // 1 when the shop is "hạn chế hiển thị" by penalty points: ranked after everything else
    int Restricted = 0);

public static class ProductSearchProjection
{
    /// <summary>Build documents for the given products; ids that are no longer buyable come back in <c>Removed</c>.</summary>
    public static async Task<(List<ProductSearchDocument> Documents, List<Guid> Removed)> BuildAsync(ShopHubDbContext db, IReadOnlyCollection<Guid> ids,
        Application.Features.Marketing.PriceBook prices, DateTimeOffset now, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.Name }).ToDictionaryAsync(c => c.Id, ct);
        var products = await db.Products.AsNoTracking().IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id) && p.DeletedAt == null && p.Status == ProductStatus.Active
                        && db.Shops.Any(s => s.Id == p.ShopId && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation)))
            .Include(p => p.Skus).Include(p => p.Media).Include(p => p.Attributes)
            .AsSplitQuery()
            .ToListAsync(ct);
        var shopIds = products.Select(p => p.ShopId).Distinct().ToList();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var restrictRaw = await db.SystemParameters.AsNoTracking().Where(x => x.Key == Application.SystemConfig.ParameterKeys.ShopPenaltyRestrictPoints)
            .Select(x => x.Value).FirstOrDefaultAsync(ct);
        var restrictAt = int.TryParse(restrictRaw, out var r) ? r : int.MaxValue;
        var provinces = await (from w in db.ShopWarehouses
                               join d in db.AdminDivisions on w.ProvinceCode equals d.Code
                               where shopIds.Contains(w.ShopId) && w.IsPickupDefault
                               select new { w.ShopId, d.Code, d.Name }).AsNoTracking().ToListAsync(ct);
        var brandIds = products.Where(p => p.BrandId != null).Select(p => p.BrandId!.Value).Distinct().ToList();
        var brands = await db.Brands.AsNoTracking().Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var attributeIds = products.SelectMany(p => p.Attributes.Select(a => a.AttributeId)).Distinct().ToList();
        var attributeNames = await db.CategoryAttributes.AsNoTracking().Where(a => attributeIds.Contains(a.Id) && a.IsFilterable)
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        // Prices in force (shop discount, flash sale) — what the cards and the checkout charge, so price filters and sorting agree
        var effective = await prices.ForSkusAsync(products.SelectMany(p => p.Skus.Where(s => s.IsActive).Select(s => s.Id)).ToList(), now, ct);

        var docs = new List<ProductSearchDocument>();
        foreach (var p in products)
        {
            var shop = shops[p.ShopId];
            var province = provinces.FirstOrDefault(x => x.ShopId == p.ShopId);
            var chain = new List<(Guid Id, string Name)>();
            for (var cur = categories.GetValueOrDefault(p.CategoryId); cur is not null; cur = cur.ParentId is { } pid ? categories.GetValueOrDefault(pid) : null)
                chain.Add((cur.Id, cur.Name));
            var activeSkus = p.Skus.Where(s => s.IsActive).ToList();
            long PriceOf(Domain.Catalog.Sku s) => effective.TryGetValue(s.Id, out var e) ? e.Price : s.Price;
            var cheapest = activeSkus.OrderBy(PriceOf).ThenBy(s => s.Id).FirstOrDefault();
            var minPrice = cheapest is null ? p.MinPrice : PriceOf(cheapest);
            var maxPrice = activeSkus.Count == 0 ? p.MaxPrice : activeSkus.Max(PriceOf);
            var original = cheapest is null ? p.MinPrice : Math.Max(cheapest.OriginalPrice, cheapest.Price);

            docs.Add(new ProductSearchDocument(
                p.Id.ToString(), p.Name, Slug.Fold(p.Name), p.Slug,
                p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                minPrice, maxPrice, original, ProductCards.DiscountPercent(minPrice, original),
                p.RatingAvg, (int)Math.Floor(p.RatingAvg), p.RatingCount, p.SoldCount,
                activeSkus.Any(s => s.Stock - s.Reserved > 0),
                shop.Id.ToString(), shop.Name, Slug.Fold(shop.Name), shop.Type == ShopType.Mall, shop.IsPreferred,
                province?.Code, ProductCards.ShortProvince(province?.Name),
                p.CategoryId.ToString(), chain.Select(c => c.Id.ToString()).ToList(), Slug.Fold(string.Join(' ', chain.Select(c => c.Name))),
                p.BrandId?.ToString(), p.BrandId is { } b ? brands.GetValueOrDefault(b) : null,
                p.Condition.ToString(),
                p.Attributes.Where(a => attributeNames.ContainsKey(a.AttributeId))
                    .SelectMany(a => a.Values.Select(v => $"{attributeNames[a.AttributeId]}={v}")).ToList(),
                (p.PublishedAt ?? p.CreatedAt).ToUnixTimeSeconds(),
                shop.PenaltyPoints >= restrictAt ? 1 : 0));
        }

        var removed = ids.Except(products.Select(p => p.Id)).ToList();
        return (docs, removed);
    }
}
