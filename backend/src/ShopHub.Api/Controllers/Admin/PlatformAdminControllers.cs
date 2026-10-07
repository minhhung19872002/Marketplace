using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.Features.Audit;
using ShopHub.Application.Features.Reports;
using ShopHub.Application.Features.Seller;
using ShopHub.Application.Security;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Sales;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Api.Controllers.Admin;

/// <summary>Tổng quan &amp; báo cáo toàn sàn (spec VI.1, VI.10). Dates are Vietnam days, both ends included.</summary>
[Route("api/admin/reports")]
public sealed class ReportsAdminController : ApiControllerBase
{
    [HttpGet("overview")]
    [RequirePermission(Permissions.ReportView)]
    [ProducesResponseType<ApiResponse<AdminOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Granularity granularity = Granularity.Day,
        CancellationToken ct = default) => OkData(await Sender.Send(new AdminOverviewQuery(from, to, granularity), ct));

    [HttpGet("{kind}")]
    [RequirePermission(Permissions.ReportView)]
    [ProducesResponseType<ApiResponse<ReportResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(AdminReportKind kind, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Granularity granularity = Granularity.Day, CancellationToken ct = default) =>
        OkData(await Sender.Send(new AdminReportQuery(kind, from, to, granularity), ct));

    /// <summary>The same table as Excel (<c>format=Xlsx</c>) or PDF.</summary>
    [HttpGet("{kind}/export")]
    [RequirePermission(Permissions.ReportView)]
    [Produces("application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    public async Task<IActionResult> Export(AdminReportKind kind, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Granularity granularity = Granularity.Day, [FromQuery] ExportFormat format = ExportFormat.Xlsx, CancellationToken ct = default)
    {
        var file = await Sender.Send(new AdminReportExportQuery(kind, from, to, granularity, format), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}

[Route("api/admin")]
public sealed class PlatformAdminController : ApiControllerBase
{
    // ---------- users ----------

    [HttpGet("users/{id:guid}")]
    [RequirePermission(Permissions.UserView)]
    [ProducesResponseType<ApiResponse<AdminUserDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UserDetail(Guid id, CancellationToken ct) => OkData(await Sender.Send(new AdminUserDetailQuery(id), ct));

    /// <summary>One-time password shown once; every session of the user is cut.</summary>
    [HttpPost("users/{id:guid}/reset-password")]
    [RequirePermission(Permissions.UserResetPassword)]
    [ProducesResponseType<ApiResponse<ResetPasswordResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct) =>
        OkData(await Sender.Send(new AdminResetPasswordCommand(id), ct), "Đã đặt lại mật khẩu. Người dùng phải đổi mật khẩu khi đăng nhập.");

    // ---------- penalty points ----------

    [HttpGet("shops/{id:guid}/penalties")]
    [RequirePermission(Permissions.ShopView)]
    [ProducesResponseType<ApiResponse<ShopPenaltiesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Penalties(Guid id, CancellationToken ct) => OkData(await Sender.Send(new ShopPenaltiesQuery(id), ct));

    public record PenaltyBody(int Points, string Reason, int? ExpiresInDays);

    [HttpPost("shops/{id:guid}/penalties")]
    [RequirePermission(Permissions.ShopPenalty)]
    [ProducesResponseType<ApiResponse<PenaltyStatus>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPenalty(Guid id, [FromBody] PenaltyBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new AddShopPenaltyCommand(id, body.Points, body.Reason, body.ExpiresInDays), ct), "Đã ghi điểm phạt.");

    public record ReasonBody(string Reason);

    [HttpPost("shop-penalties/{id:guid}/revoke")]
    [RequirePermission(Permissions.ShopPenalty)]
    [ProducesResponseType<ApiResponse<PenaltyStatus>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokePenalty(Guid id, [FromBody] ReasonBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new RevokeShopPenaltyCommand(id, body.Reason), ct), "Đã gỡ điểm phạt.");

    // ---------- orders ----------

    [HttpGet("orders")]
    [RequirePermission(Permissions.OrderView)]
    [ProducesResponseType<ApiResponse<PagedResult<AdminOrderRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Orders([FromQuery] AdminOrdersQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    [HttpGet("orders/{code}")]
    [RequirePermission(Permissions.OrderView)]
    [ProducesResponseType<ApiResponse<AdminOrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Order(string code, CancellationToken ct) => OkData(await Sender.Send(new AdminOrderDetailQuery(code), ct));

    [HttpPost("orders/{code}/cancel")]
    [RequirePermission(Permissions.OrderIntervene)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelOrder(string code, [FromBody] ReasonBody body, CancellationToken ct)
    {
        await Sender.Send(new AdminCancelOrderCommand(code, body.Reason), ct);
        return OkData<object?>(null, "Đã huỷ đơn và hoàn tiền về nguồn thanh toán (nếu đã trả).");
    }

    public record ManualRefundBody(IReadOnlyList<Application.Features.Returns.ManualRefundLineInput> Lines, long Amount, bool PlatformBorne, string Reason);

    /// <summary>Hoàn tiền thủ công cho đơn đã giao / hoàn thành: chọn dòng, số lượng, số tiền (≤ phần đã trả), ai chịu; bắt buộc lý do.</summary>
    [HttpPost("orders/{code}/manual-refund")]
    [RequirePermission(Permissions.OrderIntervene)]
    [ProducesResponseType<ApiResponse<Application.Features.Returns.ReturnDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ManualRefund(string code, [FromBody] ManualRefundBody body, CancellationToken ct) =>
        OkData(await Sender.Send(new Application.Features.Returns.AdminManualRefundCommand(code, body.Lines ?? [], body.Amount, body.PlatformBorne,
            body.Reason ?? ""), ct), "Đã hoàn tiền về nguồn thanh toán của người mua.");

    public record RefundBody(bool ToWallet, string Reason);

    /// <summary>A failed gateway refund: retry at the gateway, or send it to the buyer's Ví ShopHub.</summary>
    [HttpPost("refunds/{id:guid}/resolve")]
    [RequirePermission(Permissions.OrderIntervene)]
    [ProducesResponseType<ApiResponse<RefundStatus>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveRefund(Guid id, [FromBody] RefundBody body, CancellationToken ct)
    {
        var status = await Sender.Send(new ResolveFailedRefundCommand(id, body.ToWallet, body.Reason), ct);
        return OkData(status, status == RefundStatus.Failed ? "Cổng vẫn từ chối hoàn tiền — hãy hoàn về Ví ShopHub." : "Đã xử lý hoàn tiền.");
    }

    // ---------- catalog ----------

    [HttpGet("brands")]
    [RequirePermission(Permissions.BrandManage)]
    [ProducesResponseType<ApiResponse<PagedResult<AdminBrandDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Brands([FromQuery] AdminBrandsQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    public record BrandBody(string Name, string? LogoUrl, bool IsVerified);

    [HttpPut("brands/{id:guid}")]
    [RequirePermission(Permissions.BrandManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBrand(Guid id, [FromBody] BrandBody body, CancellationToken ct)
    {
        await Sender.Send(new UpdateBrandCommand(id, body.Name, body.LogoUrl, body.IsVerified), ct);
        return OkData<object?>(null, "Đã lưu thương hiệu.");
    }

    public record MoveBody(Guid? ParentId, int SortOrder);

    [HttpPut("categories/{id:guid}/move")]
    [RequirePermission(Permissions.CategoryManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MoveCategory(Guid id, [FromBody] MoveBody body, CancellationToken ct)
    {
        await Sender.Send(new MoveCategoryCommand(id, body.ParentId, body.SortOrder), ct);
        return OkData<object?>(null, "Đã chuyển danh mục.");
    }

    public record BulkBanBody(IReadOnlyList<Guid> ProductIds, string Reason);

    [HttpPost("products/bulk-ban")]
    [RequirePermission(Permissions.ProductBan)]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkBan([FromBody] BulkBanBody body, CancellationToken ct)
    {
        var count = await Sender.Send(new BulkBanProductsCommand(body.ProductIds ?? [], body.Reason), ct);
        return OkData(count, $"Đã khoá {count} sản phẩm.");
    }

    [HttpGet("product-reports")]
    [RequirePermission(Permissions.ProductBan)]
    [ProducesResponseType<ApiResponse<PagedResult<ProductReportDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ProductReports([FromQuery] ProductReportsQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    public record ResolveReportBody(bool Ban, string? Resolution);

    [HttpPost("product-reports/{id:guid}/resolve")]
    [RequirePermission(Permissions.ProductBan)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveReport(Guid id, [FromBody] ResolveReportBody body, CancellationToken ct)
    {
        await Sender.Send(new ResolveProductReportCommand(id, body.Ban, body.Resolution), ct);
        return OkData<object?>(null, body.Ban ? "Đã khoá sản phẩm và đóng các báo cáo." : "Đã bỏ qua báo cáo.");
    }

    // ---------- reported chats ----------

    [HttpGet("chat-reports")]
    [RequirePermission(Permissions.ChatReview)]
    [ProducesResponseType<ApiResponse<PagedResult<ChatReportRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChatReports([FromQuery] AdminChatReportsQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    /// <summary>The report and the conversation's last 200 messages; every call is written to the audit log.</summary>
    [HttpGet("chat-reports/{id:guid}")]
    [RequirePermission(Permissions.ChatReview)]
    [ProducesResponseType<ApiResponse<ChatReportDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChatReport(Guid id, CancellationToken ct) => OkData(await Sender.Send(new AdminChatReportDetailQuery(id), ct));

    public record ResolveChatReportBody(string Resolution, int? PenaltyPoints);

    [HttpPost("chat-reports/{id:guid}/resolve")]
    [RequirePermission(Permissions.ChatReview)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveChatReport(Guid id, [FromBody] ResolveChatReportBody body, CancellationToken ct)
    {
        await Sender.Send(new ResolveChatReportCommand(id, body.Resolution, body.PenaltyPoints), ct);
        return OkData<object?>(null, body.PenaltyPoints is null ? "Đã đóng báo cáo, không vi phạm." : $"Đã ghi {body.PenaltyPoints} điểm phạt cho shop.");
    }

    // ---------- danh mục hành chính ----------

    [HttpGet("divisions")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AdminDivisionRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Divisions([FromQuery] string? parent, CancellationToken ct) =>
        OkData(await Sender.Send(new AdminDivisionsQuery(string.IsNullOrWhiteSpace(parent) ? null : parent), ct));

    public record DivisionBody(string Code, string Name, string? ParentCode);

    [HttpPost("divisions")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddDivision([FromBody] DivisionBody body, CancellationToken ct)
    {
        await Sender.Send(new SaveAdminDivisionCommand(body.Code ?? "", body.Name ?? "", body.ParentCode, true), ct);
        return OkData<object?>(null, "Đã thêm đơn vị hành chính.");
    }

    public record DivisionNameBody(string Name);

    [HttpPut("divisions/{code}")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RenameDivision(string code, [FromBody] DivisionNameBody body, CancellationToken ct)
    {
        await Sender.Send(new SaveAdminDivisionCommand(code, body.Name ?? "", null, false), ct);
        return OkData<object?>(null, "Đã đổi tên đơn vị hành chính.");
    }

    // ---------- content ----------

    [HttpGet("cms")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CmsPageDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cms([FromQuery] CmsKind? kind, CancellationToken ct) => OkData(await Sender.Send(new AdminCmsPagesQuery(kind), ct));

    [HttpPost("cms")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveCms([FromBody] SaveCmsPageCommand body, CancellationToken ct) => OkData(await Sender.Send(body, ct), "Đã lưu trang.");

    [HttpGet("message-templates")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MessageTemplateDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Templates(CancellationToken ct) => OkData(await Sender.Send(new MessageTemplatesQuery(), ct));

    public record TemplateBody(string? Subject, string Body);

    [HttpPut("message-templates/{id:guid}")]
    [RequirePermission(Permissions.ContentManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTemplate(Guid id, [FromBody] TemplateBody body, CancellationToken ct)
    {
        await Sender.Send(new UpdateMessageTemplateCommand(id, body.Subject, body.Body ?? ""), ct);
        return OkData<object?>(null, "Đã lưu mẫu.");
    }

    // ---------- audit export ----------

    /// <summary>Xuất nhật ký ra Excel (6.4): queued as a background task of the signed-in admin.</summary>
    [HttpPost("audit-logs/export-tasks")]
    [RequirePermission(Permissions.AuditLogView)]
    [ProducesResponseType<ApiResponse<BackgroundTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAudit([FromBody] AuditLogsExportQuery query, CancellationToken ct) =>
        OkData(await Sender.Send(new StartAuditExportCommand(query), ct), "Đang xuất tệp, bạn sẽ tải được khi xong.");

    [HttpGet("my-tasks/{taskId:guid}")]
    [RequirePermission(Permissions.AuditLogView)]
    [ProducesResponseType<ApiResponse<BackgroundTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTask(Guid taskId, CancellationToken ct) => OkData(await Sender.Send(new MyTaskQuery(taskId), ct));

    [HttpGet("my-tasks/{taskId:guid}/file")]
    [RequirePermission(Permissions.AuditLogView)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    public async Task<IActionResult> MyTaskFile(Guid taskId, CancellationToken ct)
    {
        var file = await Sender.Send(new MyTaskFileQuery(taskId), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    // ---------- carriers & gateways ----------

    [HttpGet("providers")]
    [RequirePermission(Permissions.ProviderManage)]
    [ProducesResponseType<ApiResponse<ProvidersDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Providers(CancellationToken ct) => OkData(await Sender.Send(new ProvidersQuery(), ct));

    [HttpPut("carriers/{id:guid}")]
    [RequirePermission(Permissions.ProviderManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCarrier(Guid id, [FromBody] UpdateCarrierCommand body, CancellationToken ct)
    {
        await Sender.Send(body with { Id = id }, ct);
        return OkData<object?>(null, "Đã lưu đơn vị vận chuyển.");
    }

    public record GatewayBody(bool Enabled);

    [HttpPut("gateways/{method}")]
    [RequirePermission(Permissions.ProviderManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetGateway(PaymentMethod method, [FromBody] GatewayBody body, CancellationToken ct)
    {
        await Sender.Send(new SetGatewayEnabledCommand(method, body.Enabled), ct);
        return OkData<object?>(null, body.Enabled ? "Đã bật cổng thanh toán." : "Đã tắt cổng thanh toán (giao dịch cũ vẫn tra cứu / hoàn tiền được).");
    }
}

/// <summary>Public content: static pages and the help center (spec II.12).</summary>
[Route("api")]
public sealed class ContentController : ApiControllerBase
{
    [HttpGet("cms/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<CmsPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Page(string slug, CancellationToken ct) => OkData(await Sender.Send(new CmsPageBySlugQuery(slug), ct));

    /// <summary>Platform identity and legal entity for the footer (parameters SITE.*).</summary>
    [HttpGet("site")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SiteInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Site(CancellationToken ct) => OkData(await Sender.Send(new SiteInfoQuery(), ct));

    [HttpGet("help")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CmsSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Help([FromQuery] string? q, CancellationToken ct) => OkData(await Sender.Send(new HelpCenterQuery(q), ct));
}

[Route("api/products/{id:guid}/reports")]
[OwnerGuarded("Báo cáo vi phạm do chính người gọi gửi (reporter = user id từ token).")]
public sealed class ProductReportController : ApiControllerBase
{
    public record ReportBody(ProductReportReason Reason, string? Details);

    [HttpPost]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(Guid id, [FromBody] ReportBody body, CancellationToken ct)
    {
        await Sender.Send(new ReportProductCommand(id, body.Reason, body.Details), ct);
        return OkData<object?>(null, "Cảm ơn bạn đã báo cáo. Sàn sẽ xem xét sản phẩm này.");
    }
}

/// <summary>Dữ liệu &amp; phân tích of the shop (spec III.8).</summary>
[Route("api/seller/shops/{shopId:guid}/analytics")]
[OwnerGuarded("Số liệu của shop mà người gọi là nhân viên (ORDER.VIEW); shop khác trả 404.")]
public sealed class SellerAnalyticsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<SellerAnalyticsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid shopId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Granularity granularity = Granularity.Day,
        CancellationToken ct = default) => OkData(await Sender.Send(new SellerAnalyticsQuery(shopId, from, to, granularity), ct));

    [HttpGet("export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    public async Task<IActionResult> Export(Guid shopId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Granularity granularity = Granularity.Day, CancellationToken ct = default)
    {
        var file = await Sender.Send(new SellerAnalyticsExportQuery(shopId, from, to, granularity), ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
