using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.Features.Catalog;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Shops;

namespace ShopHub.Api.Controllers.Admin;

[Route("api/admin")]
public sealed class CatalogAdminController : ApiControllerBase
{
    // ---------- Categories, attributes, brands ----------

    [HttpGet("categories")]
    [RequirePermission(Permissions.CategoryManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategoryNodeDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories(CancellationToken ct) => OkData(await Sender.Send(new GetCategoryTreeQuery(IncludeInactive: true), ct));

    [HttpPost("categories")]
    [RequirePermission(Permissions.CategoryManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveCategory([FromBody] SaveCategoryCommand body, CancellationToken ct) =>
        OkData(await Sender.Send(body, ct), "Đã lưu danh mục.");

    [HttpPost("categories/attributes")]
    [RequirePermission(Permissions.CategoryManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveAttribute([FromBody] SaveCategoryAttributeCommand body, CancellationToken ct) =>
        OkData(await Sender.Send(body, ct), "Đã lưu thuộc tính.");

    [HttpDelete("categories/attributes/{id:guid}")]
    [RequirePermission(Permissions.CategoryManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAttribute(Guid id, CancellationToken ct)
    {
        await Sender.Send(new DeleteCategoryAttributeCommand(id), ct);
        return OkData<object?>(null, "Đã xoá thuộc tính.");
    }

    [HttpPost("brands")]
    [RequirePermission(Permissions.BrandManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveBrand([FromBody] SaveBrandCommand body, CancellationToken ct) =>
        OkData(await Sender.Send(body, ct), "Đã lưu thương hiệu.");

    // ---------- Product moderation ----------

    [HttpGet("products")]
    [RequirePermission(Permissions.ProductReview)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewQueueRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewQueue([FromQuery] ListProductsForReviewQuery query, CancellationToken ct) =>
        OkData(await Sender.Send(query, ct));

    [HttpGet("products/{id:guid}")]
    [RequirePermission(Permissions.ProductReview)]
    [ProducesResponseType<ApiResponse<SellerProductDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Product(Guid id, CancellationToken ct) => OkData(await Sender.Send(new GetProductForAdminQuery(id), ct));

    public record ReasonRequest(string? Reason);

    [HttpPost("products/{id:guid}/approve")]
    [RequirePermission(Permissions.ProductReview)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(Guid id, CancellationToken ct) => Moderate(id, ModerationAction.Approve, null, "Đã duyệt sản phẩm.", ct);

    [HttpPost("products/{id:guid}/reject")]
    [RequirePermission(Permissions.ProductReview)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid id, [FromBody] ReasonRequest body, CancellationToken ct) =>
        Moderate(id, ModerationAction.Reject, body.Reason, "Đã yêu cầu người bán sửa.", ct);

    [HttpPost("products/{id:guid}/ban")]
    [RequirePermission(Permissions.ProductBan)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Ban(Guid id, [FromBody] ReasonRequest body, CancellationToken ct) =>
        Moderate(id, ModerationAction.Ban, body.Reason, "Đã khoá sản phẩm.", ct);

    [HttpPost("products/{id:guid}/unban")]
    [RequirePermission(Permissions.ProductBan)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Unban(Guid id, CancellationToken ct) => Moderate(id, ModerationAction.Unban, null, "Đã mở khoá sản phẩm.", ct);

    private async Task<IActionResult> Moderate(Guid id, ModerationAction action, string? reason, string message, CancellationToken ct) =>
        OkData((await Sender.Send(new ModerateProductCommand(id, action, reason), ct)).ToString(), message);

    // ---------- Shops ----------

    [HttpGet("shops")]
    [RequirePermission(Permissions.ShopView)]
    [ProducesResponseType<ApiResponse<PagedResult<AdminShopRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Shops([FromQuery] ListShopsQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    /// <summary>Shop detail; KYC documents as signed URLs valid for 5 minutes.</summary>
    [HttpGet("shops/{id:guid}")]
    [RequirePermission(Permissions.ShopView)]
    [ProducesResponseType<ApiResponse<AdminShopDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Shop(Guid id, CancellationToken ct) => OkData(await Sender.Send(new GetShopForAdminQuery(id), ct));

    [HttpPost("shops/{id:guid}/approve")]
    [RequirePermission(Permissions.ShopReview)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> ApproveShop(Guid id, CancellationToken ct) => ModerateShop(id, ShopAdminAction.Approve, null, "Đã duyệt shop.", ct);

    [HttpPost("shops/{id:guid}/reject")]
    [RequirePermission(Permissions.ShopReview)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> RejectShop(Guid id, [FromBody] ReasonRequest body, CancellationToken ct) =>
        ModerateShop(id, ShopAdminAction.Reject, body.Reason, "Đã từ chối hồ sơ shop.", ct);

    [HttpPost("shops/{id:guid}/lock")]
    [RequirePermission(Permissions.ShopLock)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> LockShop(Guid id, [FromBody] ReasonRequest body, CancellationToken ct) =>
        ModerateShop(id, ShopAdminAction.Lock, body.Reason, "Đã khoá shop.", ct);

    [HttpPost("shops/{id:guid}/unlock")]
    [RequirePermission(Permissions.ShopLock)]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public Task<IActionResult> UnlockShop(Guid id, CancellationToken ct) => ModerateShop(id, ShopAdminAction.Unlock, null, "Đã mở khoá shop.", ct);

    public record LabelsRequest(bool IsMall, bool IsPreferred);

    [HttpPut("shops/{id:guid}/labels")]
    [RequirePermission(Permissions.ShopLabel)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Labels(Guid id, [FromBody] LabelsRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetShopLabelsCommand(id, body.IsMall, body.IsPreferred), ct);
        return OkData<object?>(null, "Đã cập nhật nhãn shop.");
    }

    private async Task<IActionResult> ModerateShop(Guid id, ShopAdminAction action, string? reason, string message, CancellationToken ct) =>
        OkData((await Sender.Send(new ModerateShopCommand(id, action, reason), ct)).ToString(), message);
}
