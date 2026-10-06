using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Api.Security;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Features.Seller;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Logistics;

namespace ShopHub.Api.Controllers;

[Route("api/seller")]
[OwnerGuarded("Đơn hàng của shop mà người gọi là nhân viên (ORDER.VIEW / ORDER.MANAGE); đơn của shop khác trả 404.")]
public sealed class SellerOrdersController : ApiControllerBase
{
    private const string Pdf = "application/pdf";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("shops/{shopId:guid}/dashboard")]
    [ProducesResponseType<ApiResponse<SellerDashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Dashboard(Guid shopId, CancellationToken ct) => OkData(await Sender.Send(new SellerDashboardQuery(shopId), ct));

    [HttpGet("shops/{shopId:guid}/orders")]
    [ProducesResponseType<ApiResponse<PagedResult<ShopOrderRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid shopId, [FromQuery] ShopOrderTab tab = ShopOrderTab.All, [FromQuery] string? q = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] string? carrier = null,
        [FromQuery] Domain.Sales.PaymentMethod? paymentMethod = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ListShopOrdersQuery(shopId, tab, q, from, to, carrier, paymentMethod, page, pageSize), ct));

    [HttpGet("shops/{shopId:guid}/orders/{orderId:guid}")]
    [ProducesResponseType<ApiResponse<ShopOrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid shopId, Guid orderId, CancellationToken ct) => OkData(await Sender.Send(new GetShopOrderQuery(shopId, orderId), ct));

    public record PrepareRequest(IReadOnlyList<Guid> OrderIds, PickupMethod PickupMethod, string? PickupSlot);

    /// <summary>"Chuẩn bị hàng" (one or many): confirm, book pickup / drop-off, get tracking numbers. Each order reports its own result.</summary>
    [HttpPost("shops/{shopId:guid}/orders/prepare")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PrepareResultDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Prepare(Guid shopId, [FromBody] PrepareRequest body, CancellationToken ct)
    {
        var results = await Sender.Send(new PrepareOrdersCommand(shopId, body.OrderIds ?? [], body.PickupMethod, body.PickupSlot), ct);
        var ok = results.Count(r => r.Ok);
        return OkData(results, ok == results.Count ? $"Đã chuẩn bị {ok} đơn hàng." : $"Đã chuẩn bị {ok}/{results.Count} đơn hàng.");
    }

    [HttpGet("pickup-slots")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PickupSlots(CancellationToken ct) => OkData(await Sender.Send(new PickupSlotsQuery(), ct));

    /// <summary>The carrier's own label (PDF) of one order, when the carrier provides one (GHTK).</summary>
    [HttpGet("shops/{shopId:guid}/orders/{orderId:guid}/carrier-label")]
    [Produces(Pdf, "application/json")]
    public async Task<IActionResult> CarrierLabel(Guid shopId, Guid orderId, CancellationToken ct) =>
        File(await Sender.Send(new CarrierLabelQuery(shopId, orderId), ct), Pdf, $"phieu-hang-van-chuyen-{orderId:N}.pdf");

    /// <summary>Shipping labels (PDF, one page per parcel, A6 or A5) for one or many orders: <c>?ids=…&amp;ids=…</c>.</summary>
    [HttpGet("shops/{shopId:guid}/orders/labels")]
    [Produces(Pdf, "application/json")]
    public async Task<IActionResult> Labels(Guid shopId, [FromQuery] Guid[] ids, [FromQuery] LabelSize size = LabelSize.A6, CancellationToken ct = default) =>
        File(await Sender.Send(new ShippingLabelsQuery(shopId, ids, size), ct), Pdf, $"phieu-giao-hang-{DateTime.UtcNow:yyyyMMddHHmm}.pdf");

    [HttpGet("shops/{shopId:guid}/orders/picking-list")]
    [Produces(Pdf, "application/json")]
    public async Task<IActionResult> PickingList(Guid shopId, [FromQuery] Guid[] ids, CancellationToken ct) =>
        File(await Sender.Send(new PickingListQuery(shopId, ids), ct), Pdf, $"phieu-soan-hang-{DateTime.UtcNow:yyyyMMddHHmm}.pdf");

    [HttpGet("shops/{shopId:guid}/orders/export")]
    [Produces(Xlsx, "application/json")]
    public async Task<IActionResult> Export(Guid shopId, [FromQuery] ShopOrderTab tab = ShopOrderTab.All, [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default) =>
        File(await Sender.Send(new ExportShopOrdersQuery(shopId, tab, from, to), ct), Xlsx, $"don-hang-{DateTime.UtcNow:yyyyMMddHHmm}.xlsx");

    public record ReasonRequest(string Reason);

    [HttpPost("shops/{shopId:guid}/orders/{orderId:guid}/cancel")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid shopId, Guid orderId, [FromBody] ReasonRequest body, CancellationToken ct)
    {
        await Sender.Send(new SellerCancelOrderCommand(shopId, orderId, body.Reason ?? string.Empty), ct);
        return OkData<object?>(null, "Đã huỷ đơn hàng.");
    }

    public record DecideRequest(bool Approve, string? RejectReason);

    [HttpPost("shops/{shopId:guid}/orders/{orderId:guid}/cancel-request")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Decide(Guid shopId, Guid orderId, [FromBody] DecideRequest body, CancellationToken ct)
    {
        await Sender.Send(new DecideCancelRequestCommand(shopId, orderId, body.Approve, body.RejectReason), ct);
        return OkData<object?>(null, body.Approve ? "Đã chấp thuận huỷ đơn." : "Đã từ chối yêu cầu huỷ.");
    }

    public record NoteRequest(string? Note);

    [HttpPut("shops/{shopId:guid}/orders/{orderId:guid}/note")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Note(Guid shopId, Guid orderId, [FromBody] NoteRequest body, CancellationToken ct)
    {
        await Sender.Send(new SetSellerNoteCommand(shopId, orderId, body.Note), ct);
        return OkData<object?>(null, "Đã lưu ghi chú.");
    }
}

[Route("api/orders/{code}")]
[OwnerGuarded("Thao tác trên đơn mua của chính người gọi (buyer_id lọc trong câu SQL); đơn người khác trả 404.")]
public sealed class OrderActionsController : ApiControllerBase
{
    public record ReasonRequest(string Reason);

    [HttpPost("cancel")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(string code, [FromBody] ReasonRequest body, CancellationToken ct)
    {
        await Sender.Send(new CancelMyOrderCommand(code, body.Reason ?? string.Empty), ct);
        return OkData<object?>(null, "Đã huỷ đơn hàng.");
    }

    [HttpPost("cancel-request")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestCancel(string code, [FromBody] ReasonRequest body, CancellationToken ct)
    {
        await Sender.Send(new RequestCancelCommand(code, body.Reason ?? string.Empty), ct);
        return OkData<object?>(null, "Đã gửi yêu cầu huỷ, shop sẽ phản hồi trong 24 giờ.");
    }

    [HttpPost("received")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Received(string code, CancellationToken ct)
    {
        await Sender.Send(new ConfirmReceivedCommand(code), ct);
        return OkData<object?>(null, "Cảm ơn bạn! Đơn hàng đã hoàn thành.");
    }

    [HttpPost("buy-again")]
    [ProducesResponseType<ApiResponse<BuyAgainResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BuyAgain(string code, CancellationToken ct)
    {
        var result = await Sender.Send(new BuyAgainCommand(code), ct);
        return OkData(result, result.Added > 0 ? $"Đã thêm {result.Added} sản phẩm vào giỏ hàng." : "Các sản phẩm của đơn này hiện không còn bán.");
    }
}

[Route("api")]
public sealed class LogisticsController(CarrierWebhookIntake intake) : ApiControllerBase
{
    /// <summary>Public parcel tracking by tracking number (status and events only — no names, phones or addresses).</summary>
    [HttpGet("tracking/{trackingNo}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<TrackingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Track(string trackingNo, CancellationToken ct) => OkData(await Sender.Send(new TrackShipmentQuery(trackingNo), ct));

    /// <summary>Carrier → ShopHub status notification. Authenticity comes from the carrier's signature; replays are ignored.</summary>
    [HttpPost("logistics/webhooks/{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(string provider, CancellationToken ct)
    {
        // GHN posts JSON, GHTK posts a form; both are authenticated by the secret token in the callback URL
        var (accepted, result) = await intake.HandleAsync(provider, await InboundWebhooks.ReadAsync(Request, ct), ct);
        return accepted ? Ok(new { code = 200, result }) : BadRequest(new { code = 400, result });
    }
}

[Route("api/notifications")]
[OwnerGuarded("Thông báo của chính người gọi (user id từ token).")]
public sealed class NotificationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<NotificationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] NotificationCategory? category = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => OkData(await Sender.Send(new ListNotificationsQuery(category, page, pageSize), ct));

    [HttpGet("unread")]
    [ProducesResponseType<ApiResponse<UnreadCountsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unread(CancellationToken ct) => OkData(await Sender.Send(new UnreadNotificationsQuery(), ct));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) => OkData(await Sender.Send(new MarkNotificationsReadCommand(id), ct));

    [HttpPost("read-all")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadAll(CancellationToken ct) =>
        OkData(await Sender.Send(new MarkNotificationsReadCommand(null), ct), "Đã đánh dấu tất cả là đã đọc.");
}
