using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Features.Marketing;
using ShopHub.Application.Security;
using ShopHub.Domain.Promo;

namespace ShopHub.Api.Controllers;

[Route("api")]
public sealed class MarketingController : ApiControllerBase
{
    /// <summary>The platform Flash Sale slot running now (or the next one), its items and the server time for the countdown.</summary>
    [HttpGet("flash-sale")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<FlashBoardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FlashSale([FromQuery] Guid? slotId, CancellationToken ct) => OkData(await Sender.Send(new FlashBoardQuery(slotId), ct));

    /// <summary>Programme prices of the product's SKUs, its flash sale, shop offers (combo / add-on / gift).</summary>
    [HttpGet("products/{productId:guid}/deals")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ProductDealsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deals(Guid productId, CancellationToken ct) => OkData(await Sender.Send(new ProductDealsQuery(productId), ct));

    /// <summary>Chương trình của shop đang chạy (giảm giá, combo, mua kèm, quà tặng).</summary>
    [HttpGet("shops/{shopId:guid}/offers")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ShopOfferDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShopOffers(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopOffersQuery(shopId), ct));

    [HttpGet("home/banners")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<HomeBannersDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Banners(CancellationToken ct) => OkData(await Sender.Send(new HomeBannersQuery(), ct));

    [HttpGet("campaigns/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<CampaignPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Campaign(string slug, CancellationToken ct) => OkData(await Sender.Send(new CampaignPageQuery(slug), ct));
}

[Route("api/account")]
[OwnerGuarded("Hạng thành viên và điểm danh của chính người gọi (user id từ token).")]
public sealed class LoyaltyController : ApiControllerBase
{
    [HttpGet("membership")]
    [ProducesResponseType<ApiResponse<MembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Membership(CancellationToken ct) => OkData(await Sender.Send(new MembershipQuery(), ct));

    [HttpGet("check-in")]
    [ProducesResponseType<ApiResponse<CheckInStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckInStatus(CancellationToken ct) => OkData(await Sender.Send(new CheckInStatusQuery(), ct));

    [HttpPost("check-in")]
    [ProducesResponseType<ApiResponse<CheckInStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn(CancellationToken ct) => OkData(await Sender.Send(new CheckInCommand(), ct), "Điểm danh thành công!");
}

[Route("api/seller/shops/{shopId:guid}/marketing")]
[OwnerGuarded("Chương trình khuyến mãi và Flash Sale của shop mà người gọi là nhân viên có quyền MARKETING.MANAGE; shop khác trả 404.")]
public sealed class SellerMarketingController : ApiControllerBase
{
    [HttpGet("skus")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PickSkuDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Skus(Guid shopId, [FromQuery] string? q = null, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopSkusQuery(shopId, q), ct));

    [HttpGet("promotions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PromotionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Promotions(Guid shopId, [FromQuery] PromotionType? type = null, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ShopPromotionsQuery(shopId, type), ct));

    [HttpPost("promotions")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePromotion(Guid shopId, [FromBody] PromotionInput input, CancellationToken ct) =>
        OkData(await Sender.Send(new CreatePromotionCommand(shopId, input), ct), "Đã tạo chương trình.");

    [HttpPost("promotions/{promotionId:guid}/stop")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StopPromotion(Guid shopId, Guid promotionId, CancellationToken ct)
    {
        await Sender.Send(new StopPromotionCommand(shopId, promotionId), ct);
        return OkData<object?>(null, "Đã dừng chương trình.");
    }

    [HttpGet("flash-sales")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<FlashSlotDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FlashSales(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopFlashSalesQuery(shopId), ct));

    public record ShopFlashBody(DateTimeOffset StartAt, DateTimeOffset EndAt, IReadOnlyList<FlashItemInput> Items);

    /// <summary>Flash Sale của shop: the shop's own slot, live at once.</summary>
    [HttpPost("flash-sales")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateFlashSale(Guid shopId, [FromBody] ShopFlashBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new CreateShopFlashSaleCommand(shopId, body.StartAt, body.EndAt, body.Items ?? []), ct), "Đã tạo Flash Sale của shop.");

    [HttpGet("platform-slots")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<FlashSlotDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlatformSlots(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new OpenPlatformSlotsQuery(shopId), ct));

    public record RegisterBody(IReadOnlyList<FlashItemInput> Items);

    [HttpPost("platform-slots/{slotId:guid}/items")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(Guid shopId, Guid slotId, [FromBody] RegisterBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new RegisterFlashItemsCommand(shopId, slotId, body.Items ?? []), ct), "Đã gửi đăng ký, chờ sàn duyệt.");
}

[Route("api/admin/marketing")]
public sealed class MarketingAdminController : ApiControllerBase
{
    [HttpGet("flash-slots")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<FlashSlotDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Slots([FromQuery] bool upcoming = true, CancellationToken ct = default) =>
        OkData(await Sender.Send(new AdminFlashSlotsQuery(upcoming), ct));

    [HttpPost("flash-slots")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSlot([FromBody] CreateFlashSlotCommand body, CancellationToken ct) =>
        OkData(await Sender.Send(body, ct), "Đã mở khung Flash Sale.");

    [HttpPost("flash-items/{itemId:guid}/approve")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid itemId, CancellationToken ct)
    {
        await Sender.Send(new DecideFlashItemCommand(itemId, true, null), ct);
        return OkData<object?>(null, "Đã duyệt.");
    }

    public record RejectBody(string Reason);

    [HttpPost("flash-items/{itemId:guid}/reject")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid itemId, [FromBody] RejectBody body, CancellationToken ct)
    {
        await Sender.Send(new DecideFlashItemCommand(itemId, false, body.Reason), ct);
        return OkData<object?>(null, "Đã từ chối.");
    }

    [HttpGet("banners")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BannerDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Banners([FromQuery] BannerPosition? position = null, CancellationToken ct = default) =>
        OkData(await Sender.Send(new AdminBannersQuery(position), ct));

    [HttpPost("banners")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveBanner([FromBody] SaveBannerCommand body, CancellationToken ct) => OkData(await Sender.Send(body, ct), "Đã lưu banner.");

    [HttpGet("campaigns")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CampaignDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Campaigns(CancellationToken ct) => OkData(await Sender.Send(new AdminCampaignsQuery(), ct));

    [HttpPost("campaigns")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveCampaign([FromBody] SaveCampaignCommand body, CancellationToken ct) =>
        OkData(await Sender.Send(body, ct), "Đã lưu chiến dịch.");
}
