using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Storefront;

internal static class UserGuard
{
    public static Guid Require(ICurrentUser user) =>
        user.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");
}

// ---------- Shop page & following ----------

public record ShopPageDto(ShopSummaryDto Shop, string Description, string? CoverUrl, bool IsFollowing);

public record GetShopPageQuery(string Slug) : IRequest<ShopPageDto>;

public sealed class GetShopPageHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<GetShopPageQuery, ShopPageDto>
{
    public async Task<ShopPageDto> Handle(GetShopPageQuery request, CancellationToken ct)
    {
        var shop = await db.Shops.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == request.Slug && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation), ct)
            ?? throw new NotFoundException("Không tìm thấy shop.");
        var following = currentUser.UserId is { } uid && await db.ShopFollowers.AnyAsync(f => f.ShopId == shop.Id && f.UserId == uid, ct);
        return new ShopPageDto(await Breadcrumbs.ShopAsync(db, shop, ct), shop.Description, shop.CoverUrl, following);
    }
}

public record FollowShopCommand(Guid ShopId, bool Follow) : IRequest<int>;

/// <summary>Follow / unfollow; the unique (shop, user) index makes double-follows impossible, the count is recomputed.</summary>
public sealed class FollowShopHandler(IApplicationDbContext db, ICurrentUser currentUser, ICounterRecomputer counters, IClock clock)
    : IRequestHandler<FollowShopCommand, int>
{
    public async Task<int> Handle(FollowShopCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        if (!await db.Shops.AnyAsync(s => s.Id == request.ShopId && (s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation), ct))
            throw new NotFoundException("Không tìm thấy shop.");
        if (await db.ShopStaff.AnyAsync(s => s.ShopId == request.ShopId && s.UserId == userId, ct))
            throw new ConflictException("Bạn không thể theo dõi shop của chính mình.", "OWN_SHOP");

        if (request.Follow)
        {
            if (!await db.ShopFollowers.AnyAsync(f => f.ShopId == request.ShopId && f.UserId == userId, ct))
            {
                db.ShopFollowers.Add(new ShopFollower(request.ShopId, userId, clock.UtcNow));
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (ConflictException)
                {
                    // A parallel request followed first — the end state is what the user asked for
                }
            }
        }
        else
        {
            await db.ShopFollowers.Where(f => f.ShopId == request.ShopId && f.UserId == userId).ExecuteDeleteAsync(ct);
        }

        await counters.RecomputeShopFollowersAsync(request.ShopId, ct);
        return await db.Shops.Where(s => s.Id == request.ShopId).Select(s => s.FollowerCount).SingleAsync(ct);
    }
}

public record FollowedShopsQuery : IRequest<IReadOnlyList<ShopSummaryDto>>;

public sealed class FollowedShopsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<FollowedShopsQuery, IReadOnlyList<ShopSummaryDto>>
{
    public async Task<IReadOnlyList<ShopSummaryDto>> Handle(FollowedShopsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var shops = await (from f in db.ShopFollowers
                           join s in db.Shops on f.ShopId equals s.Id
                           where f.UserId == userId
                           orderby f.CreatedAt descending, s.Id
                           select s).AsNoTracking().ToListAsync(ct);
        var result = new List<ShopSummaryDto>();
        foreach (var s in shops) result.Add(await Breadcrumbs.ShopAsync(db, s, ct));
        return result;
    }
}

// ---------- Wishlist ----------

public record ToggleWishlistCommand(Guid ProductId, bool Add) : IRequest<int>;

public sealed class ToggleWishlistHandler(IApplicationDbContext db, ICurrentUser currentUser, ICounterRecomputer counters, IClock clock)
    : IRequestHandler<ToggleWishlistCommand, int>
{
    public async Task<int> Handle(ToggleWishlistCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        if (request.Add)
        {
            if (!await ProductCards.Visible(db).AnyAsync(p => p.Id == request.ProductId, ct)) throw new NotFoundException("Không tìm thấy sản phẩm.");
            if (!await db.Wishlists.AnyAsync(w => w.UserId == userId && w.ProductId == request.ProductId, ct))
            {
                db.Wishlists.Add(new Wishlist(userId, request.ProductId, clock.UtcNow));
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (ConflictException)
                {
                    // Already liked by a parallel request
                }
            }
        }
        else
        {
            await db.Wishlists.Where(w => w.UserId == userId && w.ProductId == request.ProductId).ExecuteDeleteAsync(ct);
        }

        await counters.RecomputeProductLikesAsync(request.ProductId, ct);
        return await db.Products.Where(p => p.Id == request.ProductId).Select(p => p.LikeCount).SingleAsync(ct);
    }
}

public record WishlistIdsQuery : IRequest<IReadOnlyList<Guid>>;

public sealed class WishlistIdsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<WishlistIdsQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(WishlistIdsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        return await db.Wishlists.AsNoTracking().Where(w => w.UserId == userId).OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id)
            .Select(w => w.ProductId).Take(1000).ToListAsync(ct);
    }
}

public record WishlistQuery(int Page = 1, int PageSize = 30) : IRequest<PagedResult<ProductCardDto>>, IPagedRequest;

public sealed class WishlistValidator : AbstractValidator<WishlistQuery>
{
    public WishlistValidator() => this.ApplyPagingRules();
}

public sealed class WishlistHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<WishlistQuery, PagedResult<ProductCardDto>>
{
    public async Task<PagedResult<ProductCardDto>> Handle(WishlistQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var page = await ProductCards.Visible(db).AsNoTracking()
            .Where(p => db.Wishlists.Any(w => w.UserId == userId && w.ProductId == p.Id))
            .OrderByDescending(p => db.Wishlists.Where(w => w.UserId == userId && w.ProductId == p.Id).Select(w => w.CreatedAt).FirstOrDefault())
            .ThenBy(p => p.Id)
            .ToPagedResultAsync(ProductCards.Row(db), request, ct);
        return new PagedResult<ProductCardDto>(page.Items.Select(ProductCards.ToDto).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

// ---------- Recently viewed ----------

public record RecentlyViewedQuery(string? SessionKey, int Take = 20) : IRequest<IReadOnlyList<ProductCardDto>>;

public sealed class RecentlyViewedHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<RecentlyViewedQuery, IReadOnlyList<ProductCardDto>>
{
    public async Task<IReadOnlyList<ProductCardDto>> Handle(RecentlyViewedQuery request, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var views = db.ProductViews.AsNoTracking();
        if (userId is not null) views = views.Where(v => v.UserId == userId);
        else if (request.SessionKey is { Length: > 0 } key) views = views.Where(v => v.SessionKey == key);
        else return [];

        var latest = await views.GroupBy(v => v.ProductId)
            .Select(g => new { ProductId = g.Key, Last = g.Max(v => v.ViewedAt) })
            .OrderByDescending(x => x.Last).ThenBy(x => x.ProductId)
            .Take(Math.Clamp(request.Take, 1, 50)).ToListAsync(ct);
        var ids = latest.Select(x => x.ProductId).ToList();
        var rows = await ProductCards.Visible(db).AsNoTracking().Where(p => ids.Contains(p.Id)).Select(ProductCards.Row(db)).ToListAsync(ct);
        return ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).Where(r => r is not null).Select(r => ProductCards.ToDto(r!)).ToList();
    }
}
