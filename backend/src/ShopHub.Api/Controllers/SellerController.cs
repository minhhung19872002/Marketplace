using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Domain.Shops;

namespace ShopHub.Api.Controllers;

/// <summary>Seller Center. Every shop-scoped call checks shop_staff membership + permission inside the query.</summary>
[Route("api/seller")]
[OwnerGuarded("Kênh Người Bán: mọi truy vấn lọc theo shop mà người gọi là nhân viên (shop_staff), shop của người khác trả 404.")]
public sealed class SellerController : ApiControllerBase
{
    // ---------- Shops ----------

    [HttpGet("shops")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MyShopDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyShops(CancellationToken ct) => OkData(await Sender.Send(new ListMyShopsQuery(), ct));

    /// <summary>Apply to sell: shop info, pickup address, KYC documents (uploaded with purpose "kyc"), bank account.</summary>
    [HttpPost("shops")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterShopCommand body, CancellationToken ct) =>
        OkData(await Sender.Send(body, ct), "Đã gửi hồ sơ đăng ký bán hàng. Sàn sẽ duyệt trong 1–2 ngày làm việc.");

    public record ResubmitRequest(PersonalKycInput? Personal, BusinessKycInput? Business);

    [HttpPost("shops/{shopId:guid}/resubmit")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resubmit(Guid shopId, [FromBody] ResubmitRequest body, CancellationToken ct)
    {
        await Sender.Send(new ResubmitShopCommand(shopId, body.Personal, body.Business), ct);
        return OkData<object?>(null, "Đã gửi lại hồ sơ.");
    }

    public record ProfileRequest(string Description, Guid? LogoAssetId, Guid? CoverAssetId);

    [HttpPut("shops/{shopId:guid}/profile")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile(Guid shopId, [FromBody] ProfileRequest body, CancellationToken ct)
    {
        await Sender.Send(new UpdateShopProfileCommand(shopId, body.Description, body.LogoAssetId, body.CoverAssetId), ct);
        return OkData<object?>(null, "Đã lưu hồ sơ shop.");
    }

    public record VacationRequest(DateTimeOffset? Until);

    [HttpPut("shops/{shopId:guid}/vacation")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Vacation(Guid shopId, [FromBody] VacationRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetVacationCommand(shopId, body.Until), ct);
        return OkData<object?>(null, body.Until is null ? "Đã tắt chế độ tạm nghỉ." : "Đã bật chế độ tạm nghỉ.");
    }

    // ---------- Products ----------

    [HttpGet("shops/{shopId:guid}/products")]
    [ProducesResponseType<ApiResponse<PagedResult<SellerProductRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Products(Guid shopId, [FromQuery] SellerProductTab tab, [FromQuery] string? q,
        [FromQuery] Guid? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = PagingLimits.DefaultPageSize,
        CancellationToken ct = default) =>
        OkData(await Sender.Send(new ListSellerProductsQuery(shopId, tab, q, categoryId, page, pageSize), ct));

    [HttpGet("shops/{shopId:guid}/products/{productId:guid}")]
    [ProducesResponseType<ApiResponse<SellerProductDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Product(Guid shopId, Guid productId, CancellationToken ct) =>
        OkData(await Sender.Send(new GetSellerProductQuery(shopId, productId), ct));

    /// <summary>Create as draft. Submit for review with POST …/actions/submit.</summary>
    [HttpPost("shops/{shopId:guid}/products")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateProduct(Guid shopId, [FromBody] ProductInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new CreateProductCommand(shopId, body), ct), "Đã lưu nháp sản phẩm.");

    public record UpdateProductRequest(ProductInput Input, uint? Version);

    [HttpPut("shops/{shopId:guid}/products/{productId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProduct(Guid shopId, Guid productId, [FromBody] UpdateProductRequest body, CancellationToken ct)
    {
        await Sender.Send(new UpdateProductCommand(shopId, productId, body.Input, body.Version), ct);
        return OkData<object?>(null, "Đã lưu sản phẩm.");
    }

    /// <summary>submit | hide | show | delete</summary>
    [HttpPost("shops/{shopId:guid}/products/{productId:guid}/actions/{operation}")]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ProductAction(Guid shopId, Guid productId, SellerProductAction operation, CancellationToken ct)
    {
        var status = await Sender.Send(new ChangeProductStatusCommand(shopId, productId, operation), ct);
        return OkData(status.ToString(), operation switch
        {
            SellerProductAction.Submit => "Đã gửi duyệt sản phẩm.",
            SellerProductAction.Hide => "Đã ẩn sản phẩm.",
            SellerProductAction.Show => "Đã hiện sản phẩm.",
            _ => "Đã xoá sản phẩm.",
        });
    }

    [HttpGet("category-suggestions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategorySuggestionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SuggestCategories([FromQuery] string name, CancellationToken ct) =>
        OkData(await Sender.Send(new SuggestCategoriesQuery(name ?? string.Empty), ct));

    // ---------- Inventory ----------

    public record QuickSkuRequest(long? Price, long? OriginalPrice, int? Stock);

    [HttpPut("shops/{shopId:guid}/skus/{skuId:guid}")]
    [ProducesResponseType<ApiResponse<ProductSkuDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> QuickSku(Guid shopId, Guid skuId, [FromBody] QuickSkuRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new UpdateSkuQuickCommand(shopId, skuId, body.Price, body.OriginalPrice, body.Stock), ct), "Đã cập nhật.");

    public record AdjustRequest(int Delta, string? Note);

    /// <summary>Relative stock change (+ received, − damaged/lost). Never below what is reserved for orders.</summary>
    [HttpPost("shops/{shopId:guid}/skus/{skuId:guid}/stock-adjustments")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AdjustStock(Guid shopId, Guid skuId, [FromBody] AdjustRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new AdjustStockCommand(shopId, skuId, body.Delta, body.Note), ct), "Đã điều chỉnh tồn kho.");

    [HttpGet("shops/{shopId:guid}/skus/{skuId:guid}/movements")]
    [ProducesResponseType<ApiResponse<PagedResult<InventoryMovementDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Movements(Guid shopId, Guid skuId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ListInventoryMovementsQuery(shopId, skuId, page, pageSize), ct));
}
