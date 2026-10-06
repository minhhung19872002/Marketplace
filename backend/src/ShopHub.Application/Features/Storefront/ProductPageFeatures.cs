using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Storefront;

public record CategoryCrumbDto(Guid Id, string Name, string Slug);

public record PublicOptionDto(string Value, string? ImageUrl, bool Available);

public record PublicTierDto(string Name, IReadOnlyList<PublicOptionDto> Options);

public record PublicSkuDto(Guid Id, string? Option1, string? Option2, long Price, long OriginalPrice, int Available);

public record PublicAttributeDto(string Name, string Value);

public record PublicMediaDto(string Type, string Url, string? OptionValue);

public record ShopSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    bool IsMall,
    bool IsPreferred,
    int FollowerCount,
    int ProductCount,
    DateTimeOffset JoinedAt,
    string? ProvinceName,
    bool OnVacation,
    DateTimeOffset? VacationUntil);

public record ProductPageDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    ProductCondition Condition,
    IReadOnlyList<CategoryCrumbDto> Breadcrumb,
    string? BrandName,
    long MinPrice,
    long MaxPrice,
    long OriginalMinPrice,
    long OriginalMaxPrice,
    int DiscountPercent,
    double RatingAvg,
    int RatingCount,
    int SoldCount,
    int LikeCount,
    int TotalAvailable,
    bool IsPreorder,
    int PreorderDays,
    int WeightG,
    IReadOnlyList<PublicMediaDto> Media,
    IReadOnlyList<PublicTierDto> Tiers,
    IReadOnlyList<PublicSkuDto> Skus,
    IReadOnlyList<PublicAttributeDto> Attributes,
    ShopSummaryDto Shop,
    bool Purchasable);

internal static class Breadcrumbs
{
    public static async Task<IReadOnlyList<CategoryCrumbDto>> ForAsync(IApplicationDbContext db, Guid categoryId, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.Name, c.Slug }).ToListAsync(ct);
        var path = new List<CategoryCrumbDto>();
        for (var cur = all.FirstOrDefault(c => c.Id == categoryId); cur is not null && path.Count < 5; cur = all.FirstOrDefault(c => c.Id == cur.ParentId))
            path.Insert(0, new CategoryCrumbDto(cur.Id, cur.Name, cur.Slug));
        return path;
    }

    public static async Task<ShopSummaryDto> ShopAsync(IApplicationDbContext db, Shop s, CancellationToken ct)
    {
        var province = await db.ShopWarehouses.AsNoTracking().Where(w => w.ShopId == s.Id && w.IsPickupDefault)
            .Select(w => db.AdminDivisions.Where(d => d.Code == w.ProvinceCode).Select(d => d.Name).FirstOrDefault())
            .FirstOrDefaultAsync(ct);
        return new ShopSummaryDto(s.Id, s.Name, s.Slug, s.LogoUrl, s.Type == ShopType.Mall, s.IsPreferred, s.FollowerCount, s.ProductCount,
            s.ApprovedAt ?? s.CreatedAt, ProductCards.ShortProvince(province), s.Status == ShopStatus.Vacation, s.VacationUntil);
    }
}

// ---------- Product page ----------

public record GetProductPageQuery(Guid ProductId) : IRequest<ProductPageDto>;

public sealed class GetProductPageHandler(IApplicationDbContext db) : IRequestHandler<GetProductPageQuery, ProductPageDto>
{
    public async Task<ProductPageDto> Handle(GetProductPageQuery request, CancellationToken ct)
    {
        // Hidden, banned, draft, deleted or of a locked shop → simply not found for buyers
        var p = await ProductCards.Visible(db).AsNoTracking()
            .Include(x => x.Tiers).ThenInclude(t => t.Options)
            .Include(x => x.Skus)
            .Include(x => x.Media)
            .Include(x => x.Attributes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == request.ProductId, ct)
            ?? throw new NotFoundException("Sản phẩm không tồn tại hoặc đã ngừng bán.");
        var shop = await db.Shops.AsNoTracking().FirstAsync(s => s.Id == p.ShopId, ct);

        var options = p.Tiers.SelectMany(t => t.Options.Select(o => (t.TierIndex, o))).ToDictionary(x => x.o.Id, x => x);
        var skus = p.Skus.Where(s => s.IsActive && (s.Option1Id is null || options[s.Option1Id.Value].o.IsActive)
                                                && (s.Option2Id is null || options[s.Option2Id.Value].o.IsActive)).ToList();
        string? Val(Guid? id) => id is { } v ? options[v].o.Value : null;

        var tiers = p.Tiers.OrderBy(t => t.TierIndex).Select(t => new PublicTierDto(t.Name,
            t.Options.Where(o => o.IsActive).OrderBy(o => o.SortOrder).Select(o => new PublicOptionDto(o.Value, o.ImageUrl,
                // An option is selectable while at least one SKU containing it still has stock
                skus.Any(s => (t.TierIndex == 0 ? s.Option1Id : s.Option2Id) == o.Id && s.Available > 0))).ToList()))
            .Where(t => t.Options.Count > 0).ToList();

        var attrNames = await db.CategoryAttributes.AsNoTracking().Where(a => a.CategoryId == p.CategoryId)
            .OrderBy(a => a.SortOrder).ToDictionaryAsync(a => a.Id, a => (a.Name, a.Unit, a.SortOrder), ct);
        var attributes = p.Attributes.Where(a => attrNames.ContainsKey(a.AttributeId))
            .OrderBy(a => attrNames[a.AttributeId].SortOrder)
            .Select(a => new PublicAttributeDto(attrNames[a.AttributeId].Name,
                string.Join(", ", a.Values) + (attrNames[a.AttributeId].Unit is { } u ? $" {u}" : ""))).ToList();
        var brand = p.BrandId is { } bid ? await db.Brands.Where(b => b.Id == bid).Select(b => b.Name).FirstOrDefaultAsync(ct) : null;
        var cheapest = skus.OrderBy(s => s.Price).FirstOrDefault();

        return new ProductPageDto(p.Id, p.Name, p.Slug, p.Description, p.Condition, await Breadcrumbs.ForAsync(db, p.CategoryId, ct), brand,
            p.MinPrice, p.MaxPrice, skus.Count == 0 ? 0 : skus.Min(s => s.OriginalPrice), skus.Count == 0 ? 0 : skus.Max(s => s.OriginalPrice),
            cheapest is null ? 0 : ProductCards.DiscountPercent(cheapest.Price, cheapest.OriginalPrice),
            p.RatingAvg, p.RatingCount, p.SoldCount, p.LikeCount, skus.Sum(s => Math.Max(0, s.Available)), p.IsPreorder, p.PreorderDays, p.WeightG,
            p.Media.OrderBy(m => m.SortOrder).Select(m => new PublicMediaDto(m.Type.ToString(), m.Url,
                m.VariantOptionId is { } oid && options.TryGetValue(oid, out var o) ? o.o.Value : null)).ToList(),
            tiers,
            skus.Select(s => new PublicSkuDto(s.Id, Val(s.Option1Id), Val(s.Option2Id), s.Price, s.OriginalPrice, Math.Max(0, s.Available))).ToList(),
            attributes,
            await Breadcrumbs.ShopAsync(db, shop, ct),
            Purchasable: shop.Status == ShopStatus.Active);
    }
}

public record RecordProductViewCommand(Guid ProductId, string? SessionKey) : IRequest<Unit>;

/// <summary>Counts a view unless the same viewer saw the product within PRODUCT.VIEW_DEDUPE_MINUTES.</summary>
public sealed class RecordProductViewHandler(IApplicationDbContext db, ICurrentUser currentUser, ISystemParameters parameters, IClock clock)
    : IRequestHandler<RecordProductViewCommand, Unit>
{
    public async Task<Unit> Handle(RecordProductViewCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var session = userId is null ? request.SessionKey : null;
        if (userId is null && string.IsNullOrWhiteSpace(session)) return Unit.Value;
        if (!await ProductCards.Visible(db).AnyAsync(p => p.Id == request.ProductId, ct)) throw new NotFoundException("Không tìm thấy sản phẩm.");

        var minutes = await parameters.GetIntAsync(ParameterKeys.ProductViewDedupeMinutes, ct);
        var since = clock.UtcNow.AddMinutes(-minutes);
        // Serialised per viewer + product so parallel page loads count once
        return await db.InLockedTransactionAsync($"view:{request.ProductId}:{userId?.ToString() ?? session}", async () =>
        {
            var seen = await db.ProductViews.AnyAsync(v => v.ProductId == request.ProductId && v.ViewedAt > since
                && (userId != null ? v.UserId == userId : v.SessionKey == session), ct);
            if (seen) return Unit.Value;

            db.ProductViews.Add(new ProductView(userId, session, request.ProductId, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Unit.Value;
        }, ct);
    }
}

public record RelatedProductsQuery(Guid ProductId, int Take = 12) : IRequest<IReadOnlyList<ProductCardDto>>;

public sealed class RelatedProductsHandler(IApplicationDbContext db) : IRequestHandler<RelatedProductsQuery, IReadOnlyList<ProductCardDto>>
{
    public async Task<IReadOnlyList<ProductCardDto>> Handle(RelatedProductsQuery request, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == request.ProductId).Select(p => new { p.CategoryId, p.ShopId })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
        var take = Math.Clamp(request.Take, 1, 30);
        var rows = await ProductCards.Visible(db).AsNoTracking()
            .Where(p => p.Id != request.ProductId && p.CategoryId == product.CategoryId)
            .OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.RatingAvg).ThenByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
            .Take(take).Select(ProductCards.Row(db)).ToListAsync(ct);
        return rows.Select(ProductCards.ToDto).ToList();
    }
}

public record ShopOtherProductsQuery(Guid ProductId, int Take = 12) : IRequest<IReadOnlyList<ProductCardDto>>;

public sealed class ShopOtherProductsHandler(IApplicationDbContext db) : IRequestHandler<ShopOtherProductsQuery, IReadOnlyList<ProductCardDto>>
{
    public async Task<IReadOnlyList<ProductCardDto>> Handle(ShopOtherProductsQuery request, CancellationToken ct)
    {
        var shopId = await db.Products.AsNoTracking().Where(p => p.Id == request.ProductId).Select(p => (Guid?)p.ShopId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Không tìm thấy sản phẩm.");
        var rows = await ProductCards.Visible(db).AsNoTracking()
            .Where(p => p.ShopId == shopId && p.Id != request.ProductId)
            .OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
            .Take(Math.Clamp(request.Take, 1, 30)).Select(ProductCards.Row(db)).ToListAsync(ct);
        return rows.Select(ProductCards.ToDto).ToList();
    }
}

// ---------- Home ----------

public record RecommendationsQuery(int Page = 1, int PageSize = 24, string? SessionKey = null) : IRequest<PagedResult<ProductCardDto>>, IPagedRequest;

public sealed class RecommendationsValidator : AbstractValidator<RecommendationsQuery>
{
    public RecommendationsValidator() => this.ApplyPagingRules();
}

/// <summary>
/// "Gợi ý hôm nay": categories the viewer looked at in the last 30 days come first, then best sellers. Anonymous
/// visitors without history simply get the best sellers.
/// </summary>
public sealed class RecommendationsHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<RecommendationsQuery, PagedResult<ProductCardDto>>
{
    public async Task<PagedResult<ProductCardDto>> Handle(RecommendationsQuery request, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-30);
        var userId = currentUser.UserId;
        var views = db.ProductViews.AsNoTracking().Where(v => v.ViewedAt > since);
        views = userId is not null ? views.Where(v => v.UserId == userId)
            : request.SessionKey is { Length: > 0 } key ? views.Where(v => v.SessionKey == key)
            : views.Where(_ => false);
        var interests = await (from v in views
                               join p in db.Products on v.ProductId equals p.Id
                               group v by p.CategoryId into g
                               orderby g.Count() descending, g.Key
                               select g.Key).Take(5).ToListAsync(ct);

        var page = await ProductCards.Visible(db).AsNoTracking()
            .OrderByDescending(p => interests.Contains(p.CategoryId))
            .ThenByDescending(p => p.SoldCount).ThenByDescending(p => p.RatingAvg).ThenByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
            .ToPagedResultAsync(ProductCards.Row(db), request, ct);
        return new PagedResult<ProductCardDto>(page.Items.Select(ProductCards.ToDto).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

public record TopCategoryProductDto(CategoryCrumbDto Category, ProductCardDto Product);

public record TopProductsByCategoryQuery : IRequest<IReadOnlyList<TopCategoryProductDto>>;

/// <summary>"Tìm kiếm hàng đầu": the best-selling product of each top-level category.</summary>
public sealed class TopProductsByCategoryHandler(IApplicationDbContext db) : IRequestHandler<TopProductsByCategoryQuery, IReadOnlyList<TopCategoryProductDto>>
{
    public async Task<IReadOnlyList<TopCategoryProductDto>> Handle(TopProductsByCategoryQuery request, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => new { c.Id, c.ParentId, c.Name, c.Slug, c.SortOrder }).ToListAsync(ct);
        var result = new List<TopCategoryProductDto>();
        foreach (var top in all.Where(c => c.ParentId is null).OrderBy(c => c.SortOrder))
        {
            var leafIds = new List<Guid>();
            var frontier = new List<Guid> { top.Id };
            while (frontier.Count > 0)
            {
                var children = all.Where(c => c.ParentId is { } pid && frontier.Contains(pid)).Select(c => c.Id).ToList();
                leafIds.AddRange(frontier);
                frontier = children;
            }
            var row = await ProductCards.Visible(db).AsNoTracking().Where(p => leafIds.Contains(p.CategoryId))
                .OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.RatingAvg).ThenByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
                .Select(ProductCards.Row(db)).FirstOrDefaultAsync(ct);
            if (row is not null) result.Add(new TopCategoryProductDto(new CategoryCrumbDto(top.Id, top.Name, top.Slug), ProductCards.ToDto(row)));
        }
        return result;
    }
}

public record MallShopDto(Guid Id, string Name, string Slug, string? LogoUrl, string? CoverImageUrl, int ProductCount);

public record MallShopsQuery(int Take = 12) : IRequest<IReadOnlyList<MallShopDto>>;

public sealed class MallShopsHandler(IApplicationDbContext db) : IRequestHandler<MallShopsQuery, IReadOnlyList<MallShopDto>>
{
    public async Task<IReadOnlyList<MallShopDto>> Handle(MallShopsQuery request, CancellationToken ct) =>
        await db.Shops.AsNoTracking()
            .Where(s => s.Type == ShopType.Mall && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation))
            .OrderByDescending(s => s.ProductCount).ThenByDescending(s => s.FollowerCount).ThenBy(s => s.Name).ThenBy(s => s.Id)
            .Take(Math.Clamp(request.Take, 1, 30))
            .Select(s => new MallShopDto(s.Id, s.Name, s.Slug, s.LogoUrl,
                db.Products.Where(p => p.ShopId == s.Id && p.Status == ProductStatus.Active)
                    .OrderByDescending(p => p.SoldCount).ThenBy(p => p.Id)
                    .SelectMany(p => p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).Take(1))
                    .FirstOrDefault(),
                s.ProductCount))
            .ToListAsync(ct);
}

public record CategoryPageDto(CategoryCrumbDto Category, IReadOnlyList<CategoryCrumbDto> Breadcrumb, IReadOnlyList<CategoryCrumbDto> Children);

public record GetCategoryBySlugQuery(string Slug) : IRequest<CategoryPageDto>;

public sealed class GetCategoryBySlugHandler(IApplicationDbContext db) : IRequestHandler<GetCategoryBySlugQuery, CategoryPageDto>
{
    public async Task<CategoryPageDto> Handle(GetCategoryBySlugQuery request, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == request.Slug && c.IsActive, ct)
            ?? throw new NotFoundException("Không tìm thấy danh mục.");
        var children = await db.Categories.AsNoTracking().Where(c => c.ParentId == category.Id && c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new CategoryCrumbDto(c.Id, c.Name, c.Slug)).ToListAsync(ct);
        return new CategoryPageDto(new CategoryCrumbDto(category.Id, category.Name, category.Slug),
            await Breadcrumbs.ForAsync(db, category.Id, ct), children);
    }
}
