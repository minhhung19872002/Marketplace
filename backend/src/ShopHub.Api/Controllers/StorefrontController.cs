using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;

namespace ShopHub.Api.Controllers;

/// <summary>Public buyer-site endpoints (search, product page, home sections, shop page).</summary>
[Route("api")]
public sealed class StorefrontController : ApiControllerBase
{
    // Anonymous visitor key (views de-duplication, "đã xem gần đây" before sign-in)
    public const string VisitorCookie = "sh_vid";

    private string VisitorKey()
    {
        if (Request.Cookies.TryGetValue(VisitorCookie, out var key) && key.Length is > 10 and <= 64) return key;
        key = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(VisitorCookie, key, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api",
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
        return key;
    }

    /// <summary>
    /// Search with facet counts. Arrays repeat the key: provinces=01&amp;provinces=79, attrs=Chất liệu=Cotton.
    /// 60 results per page by default, stable order.
    /// </summary>
    [HttpGet("search/products")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ProductSearchResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] SearchProductsQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    [HttpGet("search/suggest")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SuggestionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suggest([FromQuery] string q, CancellationToken ct) => OkData(await Sender.Send(new SuggestQuery(q ?? string.Empty), ct));

    [HttpGet("search/hot-keywords")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HotKeywords(CancellationToken ct) => OkData(await Sender.Send(new HotKeywordsQuery(), ct));

    [HttpGet("products/{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ProductPageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Product(Guid id, CancellationToken ct) => OkData(await Sender.Send(new GetProductPageQuery(id), ct));

    /// <summary>Count a view (same viewer within 30 minutes counts once).</summary>
    [HttpPost("products/{id:guid}/views")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> View(Guid id, CancellationToken ct)
    {
        await Sender.Send(new RecordProductViewCommand(id, VisitorKey()), ct);
        return OkData<object?>(null);
    }

    [HttpGet("products/{id:guid}/related")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProductCardDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Related(Guid id, CancellationToken ct) => OkData(await Sender.Send(new RelatedProductsQuery(id), ct));

    [HttpGet("products/{id:guid}/shop-products")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProductCardDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShopProducts(Guid id, CancellationToken ct) => OkData(await Sender.Send(new ShopOtherProductsQuery(id), ct));

    [HttpGet("home/recommendations")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<PagedResult<ProductCardDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Recommendations([FromQuery] int page = 1, [FromQuery] int pageSize = 24, CancellationToken ct = default) =>
        OkData(await Sender.Send(new RecommendationsQuery(page, pageSize, Request.Cookies[VisitorCookie]), ct));

    [HttpGet("home/top-categories")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TopCategoryProductDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopCategories(CancellationToken ct) => OkData(await Sender.Send(new TopProductsByCategoryQuery(), ct));

    [HttpGet("home/mall")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MallShopDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mall(CancellationToken ct) => OkData(await Sender.Send(new MallShopsQuery(), ct));

    [HttpGet("viewed")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProductCardDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Viewed(CancellationToken ct) =>
        OkData(await Sender.Send(new RecentlyViewedQuery(Request.Cookies[VisitorCookie]), ct));

    [HttpGet("categories/by-slug/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<CategoryPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CategoryBySlug(string slug, CancellationToken ct) => OkData(await Sender.Send(new GetCategoryBySlugQuery(slug), ct));

    [HttpGet("shops/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ShopPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Shop(string slug, CancellationToken ct) => OkData(await Sender.Send(new GetShopPageQuery(slug), ct));
}

[Route("api")]
[OwnerGuarded("Theo dõi shop / yêu thích của chính người gọi (user id từ token, lọc trong câu SQL).")]
public sealed class EngageController : ApiControllerBase
{
    [HttpPost("shops/{id:guid}/follow")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Follow(Guid id, CancellationToken ct) =>
        OkData(await Sender.Send(new FollowShopCommand(id, true), ct), "Đã theo dõi shop.");

    [HttpDelete("shops/{id:guid}/follow")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unfollow(Guid id, CancellationToken ct) =>
        OkData(await Sender.Send(new FollowShopCommand(id, false), ct), "Đã bỏ theo dõi shop.");

    [HttpGet("account/followed-shops")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ShopSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FollowedShops(CancellationToken ct) => OkData(await Sender.Send(new FollowedShopsQuery(), ct));

    [HttpGet("account/wishlist")]
    [ProducesResponseType<ApiResponse<PagedResult<ProductCardDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Wishlist([FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        OkData(await Sender.Send(new WishlistQuery(page, pageSize), ct));

    [HttpGet("account/wishlist/ids")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<Guid>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WishlistIds(CancellationToken ct) => OkData(await Sender.Send(new WishlistIdsQuery(), ct));

    [HttpPost("account/wishlist/{productId:guid}")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Like(Guid productId, CancellationToken ct) =>
        OkData(await Sender.Send(new ToggleWishlistCommand(productId, true), ct), "Đã thêm vào yêu thích.");

    [HttpDelete("account/wishlist/{productId:guid}")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unlike(Guid productId, CancellationToken ct) =>
        OkData(await Sender.Send(new ToggleWishlistCommand(productId, false), ct), "Đã bỏ yêu thích.");
}

[Route("api/admin/search")]
public sealed class SearchAdminController(ISearchIndexer indexer) : ApiControllerBase
{
    /// <summary>Rebuild the whole search index from the database (normally automatic via the outbox).</summary>
    [HttpPost("reindex")]
    [RequirePermission(Application.Security.Permissions.SearchReindex)]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reindex(CancellationToken ct) => OkData(await indexer.ReindexAllAsync(ct), "Đã lập lại chỉ mục tìm kiếm.");
}
