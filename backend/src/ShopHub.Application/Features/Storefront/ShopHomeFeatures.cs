using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Storefront;

// The shop page's "Dạo" tab (decoration blocks) and its shop-category tabs (II.5).

public record ShopHomeBlockDto(DecorationBlockType Type, string? Title, IReadOnlyList<DecorationImage> Images, IReadOnlyList<ProductCardDto> Products,
    Guid? ShopCategoryId, string? VideoUrl, string? Text);

public record ShopHomeQuery(Guid ShopId) : IRequest<IReadOnlyList<ShopHomeBlockDto>>;

/// <summary>
/// Resolves the published layout for buyers: product blocks keep only products a buyer can see (in the chosen order),
/// a category block shows the first products of a visible category; blocks left empty are dropped.
/// </summary>
public sealed class ShopHomeHandler(IApplicationDbContext db, CardPricing pricing) : IRequestHandler<ShopHomeQuery, IReadOnlyList<ShopHomeBlockDto>>
{
    private const int CategoryPreview = 10;

    public async Task<IReadOnlyList<ShopHomeBlockDto>> Handle(ShopHomeQuery request, CancellationToken ct)
    {
        if (!await db.Shops.AnyAsync(s => s.Id == request.ShopId && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation), ct))
            throw new NotFoundException("Không tìm thấy shop.");
        var blocks = await db.ShopDecorations.AsNoTracking().Where(d => d.ShopId == request.ShopId).Select(d => d.Blocks).FirstOrDefaultAsync(ct) ?? [];
        if (blocks.Count == 0) return [];

        var picked = blocks.SelectMany(b => b.ProductIds ?? []).Distinct().ToList();
        var cards = picked.Count == 0 ? new Dictionary<Guid, ProductCardDto>()
            : (await pricing.ApplyAsync((await ProductCards.Visible(db).Where(p => picked.Contains(p.Id) && p.ShopId == request.ShopId)
                .Select(ProductCards.Row(db)).ToListAsync(ct)).Select(ProductCards.ToDto).ToList(), ct)).ToDictionary(c => c.Id);

        var categoryIds = blocks.Where(b => b.ShopCategoryId is not null).Select(b => b.ShopCategoryId!.Value).Distinct().ToList();
        var categories = await db.ShopCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id) && c.ShopId == request.ShopId && c.IsVisible)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var previews = new Dictionary<Guid, IReadOnlyList<ProductCardDto>>();
        foreach (var id in categories.Keys)
        {
            var ids = await ShopCategoryListing.Members(db, id, request.ShopId).OrderBy(m => m.SortOrder).ThenBy(m => m.Id).Select(m => m.ProductId).Take(CategoryPreview).ToListAsync(ct);
            previews[id] = await pricing.ApplyAsync(await ShopCategoryListing.CardsAsync(db, ids, ct), ct);
        }

        var result = new List<ShopHomeBlockDto>();
        foreach (var b in blocks)
        {
            switch (b.Type)
            {
                case DecorationBlockType.Products:
                    var list = (b.ProductIds ?? []).Where(cards.ContainsKey).Select(id => cards[id]).ToList();
                    if (list.Count > 0) result.Add(new ShopHomeBlockDto(b.Type, b.Title, [], list, null, null, null));
                    break;
                case DecorationBlockType.Category when b.ShopCategoryId is { } cid && categories.ContainsKey(cid) && previews[cid].Count > 0:
                    result.Add(new ShopHomeBlockDto(b.Type, b.Title ?? categories[cid], [], previews[cid], cid, null, null));
                    break;
                case DecorationBlockType.Category:
                    break;
                default:
                    result.Add(new ShopHomeBlockDto(b.Type, b.Title, b.Images ?? [], [], null, b.VideoUrl, b.Text));
                    break;
            }
        }
        return result;
    }

}

internal sealed class CategoryMember
{
    public Guid ProductId { get; init; }
    public int SortOrder { get; init; }
    public Guid Id { get; init; }
}

internal static class ShopCategoryListing
{
    /// <summary>Products of a shop category a buyer can see; callers order by (SortOrder, Id) — the shop's order, ties by the unique row id.</summary>
    public static IQueryable<CategoryMember> Members(IApplicationDbContext db, Guid categoryId, Guid shopId) =>
        (from x in db.ShopCategoryProducts
         join p in ProductCards.Visible(db) on x.ProductId equals p.Id
         where x.ShopCategoryId == categoryId && p.ShopId == shopId
         select new CategoryMember { ProductId = p.Id, SortOrder = x.SortOrder, Id = x.Id });

    /// <summary>Cards for the given products, in the given order.</summary>
    public static async Task<IReadOnlyList<ProductCardDto>> CardsAsync(IApplicationDbContext db, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await db.Products.Where(p => ids.Contains(p.Id)).Select(ProductCards.Row(db)).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        return ids.Where(byId.ContainsKey).Select(id => ProductCards.ToDto(byId[id])).ToList();
    }
}

public record ShopCategoryProductsQuery(Guid ShopId, Guid CategoryId, int Page = 1, int PageSize = 30)
    : IRequest<PagedResult<ProductCardDto>>, IPagedRequest;

public sealed class ShopCategoryProductsHandler(IApplicationDbContext db, CardPricing pricing) : IRequestHandler<ShopCategoryProductsQuery, PagedResult<ProductCardDto>>
{
    public async Task<PagedResult<ProductCardDto>> Handle(ShopCategoryProductsQuery request, CancellationToken ct)
    {
        if (!await db.ShopCategories.AnyAsync(c => c.Id == request.CategoryId && c.ShopId == request.ShopId && c.IsVisible, ct))
            throw new NotFoundException("Không tìm thấy danh mục của shop.");
        var page = await ShopCategoryListing.Members(db, request.CategoryId, request.ShopId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToPagedResultAsync(m => m.ProductId, request, ct);
        return new PagedResult<ProductCardDto>(await pricing.ApplyAsync(await ShopCategoryListing.CardsAsync(db, page.Items, ct), ct), page.TotalCount,
            page.Page, page.PageSize);
    }
}
