using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Seller;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;

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

    // ---------- Kho hàng, đa kho, đơn vị vận chuyển ----------

    [HttpGet("shops/{shopId:guid}/logistics")]
    [ProducesResponseType<ApiResponse<ShopLogisticsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logistics(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopLogisticsQuery(shopId), ct));

    public record WarehouseRequest(string Name, WarehouseInput Address, bool IsPickupDefault, bool IsReturnDefault);

    [HttpPost("shops/{shopId:guid}/warehouses")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddWarehouse(Guid shopId, [FromBody] WarehouseRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveWarehouseCommand(shopId, null, body.Name, body.Address, body.IsPickupDefault, body.IsReturnDefault), ct),
            "Đã thêm kho hàng.");

    [HttpPut("shops/{shopId:guid}/warehouses/{warehouseId:guid}")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWarehouse(Guid shopId, Guid warehouseId, [FromBody] WarehouseRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveWarehouseCommand(shopId, warehouseId, body.Name, body.Address, body.IsPickupDefault, body.IsReturnDefault), ct),
            "Đã lưu kho hàng.");

    [HttpDelete("shops/{shopId:guid}/warehouses/{warehouseId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteWarehouse(Guid shopId, Guid warehouseId, CancellationToken ct)
    {
        await Sender.Send(new DeleteWarehouseCommand(shopId, warehouseId), ct);
        return OkData<object?>(null, "Đã xoá kho hàng.");
    }

    public record MultiWarehouseRequest(bool Enabled);

    [HttpPut("shops/{shopId:guid}/multi-warehouse")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MultiWarehouse(Guid shopId, [FromBody] MultiWarehouseRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetMultiWarehouseCommand(shopId, body.Enabled), ct);
        return OkData<object?>(null, body.Enabled ? "Đã bật đa kho." : "Đã tắt đa kho.");
    }

    public record ShippingChannelRequest(bool Enabled, bool CodEnabled);

    [HttpPut("shops/{shopId:guid}/shipping-channels/{carrierCode}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShippingChannel(Guid shopId, string carrierCode, [FromBody] ShippingChannelRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetShippingChannelCommand(shopId, carrierCode, body.Enabled, body.CodEnabled), ct);
        return OkData<object?>(null, "Đã lưu đơn vị vận chuyển.");
    }

    // ---------- Staff (tài khoản phụ) ----------

    [HttpGet("shops/{shopId:guid}/staff-accounts")]
    [ProducesResponseType<ApiResponse<ShopStaffBoardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StaffAccounts(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ListShopStaffQuery(shopId), ct));

    public record StaffRequest(string Login, ShopStaffRole Role, IReadOnlyList<string>? Permissions);

    /// <summary>Mời nhân viên: returns the invitation id; the account joins only after accepting (<c>staff-invitations/{id}/accept</c>).</summary>
    [HttpPost("shops/{shopId:guid}/staff-accounts")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddStaff(Guid shopId, [FromBody] StaffRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new AddShopStaffCommand(shopId, body.Login, body.Role, body.Permissions), ct),
            "Đã gửi lời mời. Nhân viên vào shop sau khi đồng ý.");

    [HttpDelete("shops/{shopId:guid}/staff-invitations/{invitationId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvitation(Guid shopId, Guid invitationId, CancellationToken ct)
    {
        await Sender.Send(new RevokeStaffInvitationCommand(shopId, invitationId), ct);
        return OkData<object?>(null, "Đã thu hồi lời mời.");
    }

    /// <summary>Invitations waiting for the signed-in user (to work as staff of a shop).</summary>
    [HttpGet("staff-invitations")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MyStaffInvitationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyInvitations(CancellationToken ct) => OkData(await Sender.Send(new MyStaffInvitationsQuery(), ct));

    [HttpPost("staff-invitations/{invitationId:guid}/accept")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitation(Guid invitationId, CancellationToken ct) =>
        OkData((await Sender.Send(new AnswerStaffInvitationCommand(invitationId, true), ct))!.Value, "Bạn đã tham gia shop.");

    [HttpPost("staff-invitations/{invitationId:guid}/decline")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclineInvitation(Guid invitationId, CancellationToken ct)
    {
        await Sender.Send(new AnswerStaffInvitationCommand(invitationId, false), ct);
        return OkData<object?>(null, "Đã từ chối lời mời.");
    }

    public record StaffChangeRequest(ShopStaffRole Role, IReadOnlyList<string>? Permissions);

    [HttpPut("shops/{shopId:guid}/staff-accounts/{staffId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStaff(Guid shopId, Guid staffId, [FromBody] StaffChangeRequest body, CancellationToken ct)
    {
        await Sender.Send(new UpdateShopStaffCommand(shopId, staffId, body.Role, body.Permissions), ct);
        return OkData<object?>(null, "Đã lưu quyền của nhân viên.");
    }

    [HttpDelete("shops/{shopId:guid}/staff-accounts/{staffId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveStaff(Guid shopId, Guid staffId, CancellationToken ct)
    {
        await Sender.Send(new RemoveShopStaffCommand(shopId, staffId), ct);
        return OkData<object?>(null, "Đã gỡ nhân viên khỏi shop.");
    }

    // ---------- Shop categories & decoration (thiết lập / trang trí shop) ----------

    [HttpGet("shops/{shopId:guid}/shop-categories")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ShopCategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShopCategories(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ListShopCategoriesQuery(shopId), ct));

    public record ShopCategoryRequest(string Name, int SortOrder, bool IsVisible = true);

    [HttpPost("shops/{shopId:guid}/shop-categories")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateShopCategory(Guid shopId, [FromBody] ShopCategoryRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveShopCategoryCommand(shopId, null, body.Name, body.SortOrder, body.IsVisible), ct), "Đã thêm danh mục.");

    [HttpPut("shops/{shopId:guid}/shop-categories/{id:guid}")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateShopCategory(Guid shopId, Guid id, [FromBody] ShopCategoryRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveShopCategoryCommand(shopId, id, body.Name, body.SortOrder, body.IsVisible), ct), "Đã lưu danh mục.");

    [HttpDelete("shops/{shopId:guid}/shop-categories/{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteShopCategory(Guid shopId, Guid id, CancellationToken ct)
    {
        await Sender.Send(new DeleteShopCategoryCommand(shopId, id), ct);
        return OkData<object?>(null, "Đã xoá danh mục.");
    }

    [HttpGet("shops/{shopId:guid}/shop-categories/{id:guid}/products")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DecorationProductDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShopCategoryMembers(Guid shopId, Guid id, CancellationToken ct) =>
        OkData(await Sender.Send(new ShopCategoryMembersQuery(shopId, id), ct));

    public record ShopCategoryProductsRequest(IReadOnlyList<Guid> ProductIds);

    [HttpPut("shops/{shopId:guid}/shop-categories/{id:guid}/products")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetShopCategoryProducts(Guid shopId, Guid id, [FromBody] ShopCategoryProductsRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SetShopCategoryProductsCommand(shopId, id, body.ProductIds), ct), "Đã lưu sản phẩm của danh mục.");

    [HttpGet("shops/{shopId:guid}/decoration")]
    [ProducesResponseType<ApiResponse<ShopDecorationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Decoration(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new GetShopDecorationQuery(shopId), ct));

    public record DecorationRequest(IReadOnlyList<DecorationBlockInput> Blocks);

    [HttpPut("shops/{shopId:guid}/decoration")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDecoration(Guid shopId, [FromBody] DecorationRequest body, CancellationToken ct)
    {
        await Sender.Send(new SaveShopDecorationCommand(shopId, body.Blocks), ct);
        return OkData<object?>(null, "Đã đăng trang trí shop.");
    }

    // ---------- Excel hàng loạt (đăng sản phẩm, cập nhật giá / tồn) ----------

    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxSheetBytes = 5 * 1024 * 1024;

    /// <summary>Import template of one leaf category (columns from its attributes, dropdowns for choices).</summary>
    [HttpGet("shops/{shopId:guid}/bulk/template")]
    [Produces(Xlsx, "application/json")]
    public async Task<IActionResult> BulkTemplate(Guid shopId, [FromQuery] Guid categoryId, CancellationToken ct)
    {
        var (name, content) = await Sender.Send(new ImportTemplateQuery(shopId, categoryId), ct);
        return File(content, Xlsx, name);
    }

    /// <summary>The shop's active SKUs with price and stock, to edit and upload back.</summary>
    [HttpGet("shops/{shopId:guid}/bulk/price-stock")]
    [Produces(Xlsx, "application/json")]
    public async Task<IActionResult> BulkPriceStock(Guid shopId, CancellationToken ct)
    {
        var (name, content) = await Sender.Send(new PriceStockExportQuery(shopId), ct);
        return File(content, Xlsx, name);
    }

    /// <summary>Upload a filled sheet (multipart "file"); the work runs in the background — follow it with GET …/bulk/tasks/{id}.</summary>
    [HttpPost("shops/{shopId:guid}/bulk/{kind}")]
    [RequestSizeLimit(MaxSheetBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSheetBytes + 64 * 1024)]
    [ProducesResponseType<ApiResponse<BackgroundTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkStart(Guid shopId, BackgroundTaskKind kind, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("Vui lòng chọn tệp Excel.", [new ApiError("file", "Vui lòng chọn tệp Excel.")]));
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return OkData(await Sender.Send(new StartBulkTaskCommand(shopId, kind, file.FileName, ms.ToArray()), ct), "Đã nhận tệp, đang xử lý.");
    }

    [HttpGet("shops/{shopId:guid}/bulk/tasks")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BackgroundTaskDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkTasks(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new BulkTasksQuery(shopId), ct));

    [HttpGet("shops/{shopId:guid}/bulk/tasks/{taskId:guid}")]
    [ProducesResponseType<ApiResponse<BackgroundTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkTask(Guid shopId, Guid taskId, CancellationToken ct) => OkData(await Sender.Send(new BulkTaskQuery(shopId, taskId), ct));

    // ---------- Freeship Xtra / Voucher Xtra ----------

    [HttpGet("shops/{shopId:guid}/xtra")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<XtraProgramDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Xtra(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ShopXtraQuery(shopId), ct));

    public record XtraRequest(bool Join);

    [HttpPut("shops/{shopId:guid}/xtra/{program}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetXtra(Guid shopId, XtraProgram program, [FromBody] XtraRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetShopXtraCommand(shopId, program, body.Join), ct);
        var name = program == XtraProgram.FreeshipXtra ? "Freeship Xtra" : "Voucher Xtra";
        return OkData<object?>(null, body.Join ? $"Đã tham gia {name}." : $"Đã rời {name}.");
    }

    // ---------- Products ----------

    [HttpGet("shops/{shopId:guid}/products")]
    [ProducesResponseType<ApiResponse<PagedResult<SellerProductRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Products(Guid shopId, [FromQuery] SellerProductTab tab, [FromQuery] string? q,
        [FromQuery] Guid? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = PagingLimits.DefaultPageSize,
        [FromQuery] int? minStock = null, [FromQuery] int? maxStock = null, [FromQuery] long? minPrice = null, [FromQuery] long? maxPrice = null,
        CancellationToken ct = default) =>
        OkData(await Sender.Send(new ListSellerProductsQuery(shopId, tab, q, categoryId, page, pageSize, minStock, maxStock, minPrice, maxPrice), ct));

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

    public record BulkProductRequest(IReadOnlyList<Guid> ProductIds, SellerProductAction Action);

    /// <summary>Ẩn / hiện / xoá / gửi duyệt nhiều sản phẩm một lần (tối đa 100); kết quả từng sản phẩm.</summary>
    [HttpPost("shops/{shopId:guid}/products/bulk-actions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BulkProductResultDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkProductAction(Guid shopId, [FromBody] BulkProductRequest body, CancellationToken ct)
    {
        var results = await Sender.Send(new BulkProductActionCommand(shopId, body.ProductIds ?? [], body.Action), ct);
        return OkData(results, $"Đã xử lý {results.Count(r => r.Ok)}/{results.Count} sản phẩm.");
    }

    public record BulkCopyRequest(IReadOnlyList<Guid> ProductIds);

    /// <summary>Sao chép nhiều sản phẩm một lần (tối đa 20): mỗi bản sao là bản nháp, tồn kho 0; kết quả từng sản phẩm.</summary>
    [HttpPost("shops/{shopId:guid}/products/bulk-copy")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BulkCopyResultDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkCopyProducts(Guid shopId, [FromBody] BulkCopyRequest body, CancellationToken ct)
    {
        var results = await Sender.Send(new BulkCopyProductsCommand(shopId, body.ProductIds ?? []), ct);
        return OkData(results, $"Đã tạo {results.Count(r => r.Ok)}/{results.Count} bản sao (bản nháp, tồn kho 0).");
    }

    [HttpPost("shops/{shopId:guid}/products/{productId:guid}/copy")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CopyProduct(Guid shopId, Guid productId, CancellationToken ct) =>
        OkData(await Sender.Send(new CopyProductCommand(shopId, productId), ct), "Đã tạo bản sao (bản nháp, tồn kho 0).");

    public record LowStockRequest(int? Units);

    [HttpPut("shops/{shopId:guid}/low-stock-threshold")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LowStockThreshold(Guid shopId, [FromBody] LowStockRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetLowStockThresholdCommand(shopId, body.Units), ct);
        return OkData<object?>(null, "Đã lưu ngưỡng sắp hết hàng.");
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
