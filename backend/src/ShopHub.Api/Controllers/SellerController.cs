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

    // ---------- Staff (tài khoản phụ) ----------

    [HttpGet("shops/{shopId:guid}/staff-accounts")]
    [ProducesResponseType<ApiResponse<ShopStaffBoardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StaffAccounts(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new ListShopStaffQuery(shopId), ct));

    public record StaffRequest(string Login, ShopStaffRole Role, IReadOnlyList<string>? Permissions);

    [HttpPost("shops/{shopId:guid}/staff-accounts")]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddStaff(Guid shopId, [FromBody] StaffRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new AddShopStaffCommand(shopId, body.Login, body.Role, body.Permissions), ct), "Đã thêm nhân viên.");

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
