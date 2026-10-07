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

    /// <summary>Sửa a programme that has not started yet (same type); a running one answers 409.</summary>
    [HttpPut("promotions/{promotionId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePromotion(Guid shopId, Guid promotionId, [FromBody] PromotionInput input, CancellationToken ct)
    {
        await Sender.Send(new UpdatePromotionCommand(shopId, promotionId, input), ct);
        return OkData<object?>(null, "Đã lưu chương trình.");
    }

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

    /// <summary>Sửa the shop's own Flash Sale before it starts; a running one answers 409.</summary>
    [HttpPut("flash-sales/{slotId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateFlashSale(Guid shopId, Guid slotId, [FromBody] ShopFlashBody body, CancellationToken ct)
    {
        await Sender.Send(new UpdateShopFlashSaleCommand(shopId, slotId, body.StartAt, body.EndAt, body.Items ?? []), ct);
        return OkData<object?>(null, "Đã lưu Flash Sale của shop.");
    }

    [HttpGet("platform-slots")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<FlashSlotDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlatformSlots(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new OpenPlatformSlotsQuery(shopId), ct));

    public record RegisterBody(IReadOnlyList<FlashItemInput> Items);

    [HttpPost("platform-slots/{slotId:guid}/items")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(Guid shopId, Guid slotId, [FromBody] RegisterBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new RegisterFlashItemsCommand(shopId, slotId, body.Items ?? []), ct), "Đã gửi đăng ký, chờ sàn duyệt.");
    // ---------- chiến dịch của sàn (III.5) ----------

    [HttpGet("campaigns")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<OpenCampaignDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenCampaigns(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopCampaignsQuery(shopId), ct));

    [HttpGet("campaigns/{campaignId:guid}/registrations")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CampaignRegistrationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CampaignRegistrations(Guid shopId, Guid campaignId, CancellationToken ct) =>
        OkData(await Sender.Send(new ShopCampaignRegistrationsQuery(shopId, campaignId), ct));

    public record CampaignRegisterBody(IReadOnlyList<Guid> ProductIds);

    [HttpPost("campaigns/{campaignId:guid}/registrations")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterCampaign(Guid shopId, Guid campaignId, [FromBody] CampaignRegisterBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new RegisterCampaignProductsCommand(shopId, campaignId, body.ProductIds ?? []), ct), "Đã gửi đăng ký, chờ sàn duyệt.");

    [HttpDelete("campaign-registrations/{registrationId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WithdrawCampaign(Guid shopId, Guid registrationId, CancellationToken ct)
    {
        await Sender.Send(new WithdrawCampaignProductCommand(shopId, registrationId), ct);
        return OkData<object?>(null, "Đã rút sản phẩm khỏi chiến dịch.");
    }
}

[Route("api/admin/marketing")]
public sealed class MarketingAdminController : ApiControllerBase
{
    /// <summary>Từ khoá hot thủ công — topped up under the search box while real search traffic is thin.</summary>
    [HttpGet("hot-keywords")]
    [RequirePermission(Permissions.HotKeywordManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HotKeywords(CancellationToken ct) => OkData(await Sender.Send(new HotKeywordsQuery(), ct));

    public record HotKeywordsBody(IReadOnlyList<string> Keywords);

    [HttpPut("hot-keywords")]
    [RequirePermission(Permissions.HotKeywordManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetHotKeywords([FromBody] HotKeywordsBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new SetHotKeywordsCommand(body.Keywords ?? []), ct), "Đã lưu từ khoá hot.");

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
    [HttpGet("campaigns/{campaignId:guid}/registrations")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<Application.Common.PagedResult<CampaignRegistrationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CampaignRegistrations(Guid campaignId, [FromQuery] Domain.Promo.CampaignRegistrationStatus? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new AdminCampaignRegistrationsQuery(campaignId, status, page, pageSize), ct));

    public record DecideRegistrationsBody(IReadOnlyList<Guid> RegistrationIds, bool Approve, string? Reason);

    [HttpPost("campaigns/{campaignId:guid}/registrations/decisions")]
    [RequirePermission(Permissions.MarketingManage)]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DecideRegistrations(Guid campaignId, [FromBody] DecideRegistrationsBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new DecideCampaignRegistrationsCommand(campaignId, body.RegistrationIds ?? [], body.Approve, body.Reason), ct),
            body.Approve ? "Đã duyệt." : "Đã từ chối.");
}
