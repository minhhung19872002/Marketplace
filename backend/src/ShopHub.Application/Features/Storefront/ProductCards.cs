using System.Linq.Expressions;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Storefront;

/// <summary>Shared EF projection of a buyable product to the card shown in every grid.</summary>
public static class ProductCards
{
    /// <summary>Products a buyer may see: active, of a shop that is active or on vacation.</summary>
    public static IQueryable<Product> Visible(IApplicationDbContext db) =>
        db.Products.Where(p => p.Status == ProductStatus.Active
                               && db.Shops.Any(s => s.Id == p.ShopId && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation)));

    public static Expression<Func<Product, ProductCardRow>> Row(IApplicationDbContext db) => p => new ProductCardRow(
        p.Id,
        p.Name,
        p.Slug,
        p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
        p.MinPrice,
        p.MaxPrice,
        p.Skus.Where(s => s.IsActive).OrderBy(s => s.Price).Select(s => s.OriginalPrice).FirstOrDefault(),
        p.RatingAvg,
        p.RatingCount,
        p.SoldCount,
        p.Skus.Any(s => s.IsActive && s.Stock - s.Reserved > 0),
        p.ShopId,
        db.Shops.Where(s => s.Id == p.ShopId).Select(s => s.Name).FirstOrDefault() ?? "",
        db.Shops.Where(s => s.Id == p.ShopId).Select(s => s.Type == ShopType.Mall).FirstOrDefault(),
        db.Shops.Where(s => s.Id == p.ShopId).Select(s => s.IsPreferred).FirstOrDefault(),
        // Nơi bán: the warehouse the product ships from (its own one when the shop runs đa kho)
        db.ShopWarehouses.Where(w => w.ShopId == p.ShopId
                                     && (db.Shops.Any(s => s.Id == p.ShopId && s.MultiWarehouse) && p.WarehouseId != null && db.ShopWarehouses.Any(x => x.Id == p.WarehouseId && x.ShopId == p.ShopId) ? w.Id == p.WarehouseId : w.IsPickupDefault))
            .Select(w => db.AdminDivisions.Where(d => d.Code == w.ProvinceCode).Select(d => d.Name).FirstOrDefault())
            .FirstOrDefault());

    /// <summary>
    /// The same item listed by several shops ("Nước Ngọt Có Ga Chai 1.5L" and "Nước Ngọt Có Ga Chai 1.5L - Giao Nhanh"):
    /// its name without what a shop appends after " - ", case and tones folded.
    /// </summary>
    public static string ModelKey(string name)
    {
        var cut = name.IndexOf(" - ", StringComparison.Ordinal);
        return Slug.Fold(cut > 0 ? name[..cut] : name);
    }

    /// <summary>
    /// G3 B2: a page of cards reordered so that the same item never shows twice within <paramref name="window"/> cards (a
    /// row of the grid): each place takes the first waiting card whose item is not among the last window − 1 placed;
    /// when every waiting item is that recent, the one placed longest ago goes in. Nothing is dropped and the order is
    /// otherwise kept, so paging and counts stay exact.
    /// </summary>
    public static IReadOnlyList<ProductCardDto> Diversify(IReadOnlyList<ProductCardDto> cards, int window = 6)
    {
        if (cards.Count < 3) return cards;
        var waiting = cards.Select(c => (Card: c, Key: ModelKey(c.Name))).ToList();
        var placed = new List<ProductCardDto>(cards.Count);
        var lastAt = new Dictionary<string, int>();
        while (waiting.Count > 0)
        {
            int Last(string key) => lastAt.TryGetValue(key, out var at) ? at : int.MinValue;
            var i = waiting.FindIndex(w => placed.Count - Last(w.Key) >= window);
            if (i < 0)
            {
                // Every item left was shown within the window: take the one seen longest ago (first in order on a tie)
                var oldest = waiting.Min(w => Last(w.Key));
                i = waiting.FindIndex(w => Last(w.Key) == oldest);
            }
            var (card, key) = waiting[i];
            waiting.RemoveAt(i);
            lastAt[key] = placed.Count;
            placed.Add(card);
        }
        return placed;
    }

    public static ProductCardDto ToDto(ProductCardRow r) => new(r.Id, r.Name, r.Slug, r.ImageUrl, r.MinPrice, r.MaxPrice,
        Math.Max(r.OriginalPrice, r.MinPrice), DiscountPercent(r.MinPrice, r.OriginalPrice), r.RatingAvg, r.RatingCount, r.SoldCount,
        r.InStock, r.ShopId, r.ShopName, r.IsMall, r.IsPreferred, ShortProvince(r.ProvinceName));

    public static int DiscountPercent(long price, long original) =>
        original <= price || original == 0 ? 0 : (int)Math.Round((original - price) * 100.0 / original, MidpointRounding.AwayFromZero);

    /// <summary>"Thành phố Hà Nội" → "Hà Nội", "Tỉnh Bình Dương" → "Bình Dương" (as shown on cards).</summary>
    public static string? ShortProvince(string? name) => name switch
    {
        null => null,
        _ when name.StartsWith("Thành phố ", StringComparison.Ordinal) => name["Thành phố ".Length..],
        _ when name.StartsWith("Tỉnh ", StringComparison.Ordinal) => name["Tỉnh ".Length..],
        _ => name,
    };
}

public record ProductCardRow(
    Guid Id,
    string Name,
    string Slug,
    string? ImageUrl,
    long MinPrice,
    long MaxPrice,
    long OriginalPrice,
    double RatingAvg,
    int RatingCount,
    int SoldCount,
    bool InStock,
    Guid ShopId,
    string ShopName,
    bool IsMall,
    bool IsPreferred,
    string? ProvinceName);
