using System.Linq.Expressions;
using ShopHub.Application.Abstractions;
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
        db.ShopWarehouses.Where(w => w.ShopId == p.ShopId && w.IsPickupDefault)
            .Select(w => db.AdminDivisions.Where(d => d.Code == w.ProvinceCode).Select(d => d.Name).FirstOrDefault())
            .FirstOrDefault());

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
