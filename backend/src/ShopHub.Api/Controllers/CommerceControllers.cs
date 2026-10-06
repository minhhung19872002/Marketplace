using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShopHub.Api.Common;
using ShopHub.Api.Hosting;
using ShopHub.Api.Security;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Cart;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Orders;
using ShopHub.Application.Features.Payments;
using ShopHub.Application.Features.Promo;
using ShopHub.Infrastructure.Commerce;

namespace ShopHub.Api.Controllers;

/// <summary>
/// Cart of the signed-in buyer, or of a guest identified by the httpOnly cookie <c>sh_cart</c>. A guest cart is
/// merged into the account at sign-in. Only the caller's own cart is ever read (owner from token or cookie).
/// </summary>
[Route("api/cart")]
[AllowAnonymous]
public sealed class CartController(ICurrentUser currentUser) : ApiControllerBase
{
    public const string GuestCookie = "sh_cart";

    private CartOwner Owner(bool create)
    {
        if (currentUser.UserId is { } userId) return new CartOwner(userId, null);
        if (Request.Cookies.TryGetValue(GuestCookie, out var token) && token.Length is >= 20 and <= 64) return new CartOwner(null, token);
        if (!create) return new CartOwner(null, null);
        token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        Response.Cookies.Append(GuestCookie, token, GuestCookieOptions(Request));
        return new CartOwner(null, token);
    }

    // Path=/api so the sign-in endpoints (/api/auth/*) also receive it and can merge the guest cart
    public static CookieOptions GuestCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/api",
        Expires = DateTimeOffset.UtcNow.AddDays(30),
    };

    [HttpGet]
    [ProducesResponseType<ApiResponse<CartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => OkData(await Sender.Send(new GetCartQuery(Owner(false)), ct));

    public record AddItemRequest(Guid SkuId, int Quantity);

    [HttpPost("items")]
    [ProducesResponseType<ApiResponse<CartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Add([FromBody] AddItemRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new AddCartItemCommand(Owner(true), body.SkuId, body.Quantity), ct), "Đã thêm vào giỏ hàng.");

    public record UpdateItemRequest(int? Quantity, bool? Selected, Guid? SkuId);

    /// <summary>Quantity, tick, or <c>skuId</c> = switch to another variant of the same product.</summary>
    [HttpPut("items/{skuId:guid}")]
    [ProducesResponseType<ApiResponse<CartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid skuId, [FromBody] UpdateItemRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new UpdateCartItemCommand(Owner(false), skuId, body.Quantity, body.Selected, body.SkuId), ct));

    [HttpDelete("items/{skuId:guid}")]
    [ProducesResponseType<ApiResponse<CartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Remove(Guid skuId, CancellationToken ct) =>
        OkData(await Sender.Send(new RemoveCartItemsCommand(Owner(false), [skuId]), ct), "Đã xoá khỏi giỏ hàng.");

    public record RemoveManyRequest(IReadOnlyList<Guid> SkuIds);

    [HttpPost("items/remove")]
    [ProducesResponseType<ApiResponse<CartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveMany([FromBody] RemoveManyRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new RemoveCartItemsCommand(Owner(false), body.SkuIds ?? []), ct), "Đã xoá khỏi giỏ hàng.");

    public record SelectRequest(Guid? ShopId, bool Selected);

    [HttpPut("selection")]
    [ProducesResponseType<ApiResponse<CartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Select([FromBody] SelectRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new SelectCartItemsCommand(Owner(false), body.ShopId, body.Selected), ct));
}

[Route("api/checkout")]
[OwnerGuarded("Thanh toán giỏ hàng của chính người gọi; checkout/đơn của người khác trả 404 (lọc user_id trong câu SQL).")]
public sealed class CheckoutController : ApiControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Price the ticked cart lines with the chosen address, carriers, vouchers, xu and payment method.</summary>
    [HttpPost("quote")]
    [ProducesResponseType<ApiResponse<CheckoutQuoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Quote([FromBody] CheckoutRequest body, CancellationToken ct) =>
        OkData(await Sender.Send(new QuoteCheckoutQuery(body), ct));

    public record PlaceRequest(CheckoutRequest Checkout, long ExpectedGrandTotal, string? WalletPin = null);

    /// <summary>
    /// Place the orders. Requires <c>Idempotency-Key</c>: the same key returns the same checkout, never a second one.
    /// 409 <c>PRICE_CHANGED</c> carries the new quote in <c>data</c> when the total moved.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(ApiServiceExtensions.CheckoutRateLimit)]
    [ProducesResponseType<ApiResponse<CheckoutResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<CheckoutQuoteDto>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Place([FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, [FromBody] PlaceRequest body,
        CancellationToken ct) =>
        OkData(await Sender.Send(new PlaceOrderCommand(idempotencyKey ?? string.Empty, body.Checkout, body.ExpectedGrandTotal, body.WalletPin), ct),
            "Đặt hàng thành công.");

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<CheckoutResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => OkData(await Sender.Send(new GetCheckoutQuery(id), ct));

    /// <summary>"Thanh toán lại" after a failed attempt, while the payment window is open.</summary>
    [HttpPost("{id:guid}/pay")]
    [ProducesResponseType<ApiResponse<CheckoutResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct) => OkData(await Sender.Send(new RetryPaymentCommand(id), ct));
}

[Route("api/orders")]
[OwnerGuarded("Đơn mua của chính người gọi (buyer_id lọc trong câu SQL); đơn của người khác trả 404.")]
public sealed class OrdersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<OrderSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListMyOrdersQuery query, CancellationToken ct) => OkData(await Sender.Send(query, ct));

    [HttpGet("{code}")]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string code, CancellationToken ct) => OkData(await Sender.Send(new GetMyOrderQuery(code), ct));
}

[Route("api/payments")]
public sealed class PaymentsController(PaymentWebhookIntake intake) : ApiControllerBase
{
    /// <summary>
    /// Gateway → ShopHub notification (IPN). Authenticity comes from the gateway's signature, not from a login; each
    /// event is applied once. The reply body follows the gateway's own format.
    /// </summary>
    [HttpPost("webhooks/{provider}")]
    [HttpGet("webhooks/{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(string provider, CancellationToken ct)
    {
        // VNPay calls the IPN with GET + signed query string, MoMo and the simulated gateway POST a signed JSON body
        var reply = await intake.HandleAsync(provider, await InboundWebhooks.ReadAsync(Request, ct), ct);
        return reply.StatusCode == StatusCodes.Status204NoContent
            ? NoContent()
            : new ContentResult { Content = reply.Body, ContentType = "application/json", StatusCode = reply.StatusCode };
    }

    /// <summary>The fake gateway's page data (only when SH_PAYMENT_SIMULATED=true). Knowing the payment id is the "card".</summary>
    [HttpGet("simulated/{paymentId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SimulatedPaymentView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Simulated(Guid paymentId, CancellationToken ct) =>
        OkData(await Desk().ViewAsync(paymentId, ct));

    /// <summary>Press "Thành công" (<c>success</c>) or "Thất bại" (<c>fail</c>) on the fake gateway page.</summary>
    [HttpPost("simulated/{paymentId:guid}/{outcome}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SimulatedPaymentView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SimulatedComplete(Guid paymentId, string outcome, CancellationToken ct)
    {
        if (outcome is not ("success" or "fail")) throw new NotFoundException("Không tìm thấy đường dẫn yêu cầu.");
        var view = await Desk().CompleteAsync(paymentId, outcome == "success", ct);
        return OkData(view, outcome == "success" ? "Thanh toán thành công." : "Thanh toán thất bại.");
    }

    private SimulatedGatewayDesk Desk() =>
        HttpContext.RequestServices.GetService<SimulatedGatewayDesk>() ?? throw new NotFoundException("Cổng thanh toán giả lập đang tắt.");
}

[Route("api/vouchers")]
[AllowAnonymous]
public sealed class VouchersController : ApiControllerBase
{
    /// <summary>Running public vouchers of the platform, or of a shop (<c>?shopId=</c>). Signed-in buyers also see why one does not apply.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<WalletVoucherDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Available([FromQuery] Guid? shopId, CancellationToken ct) =>
        OkData(await Sender.Send(new AvailableVouchersQuery(shopId), ct));
}

[Route("api/account")]
[OwnerGuarded("Ví voucher và ShopHub Xu của chính người gọi (user id từ token).")]
public sealed class WalletController : ApiControllerBase
{
    [HttpPost("vouchers/{voucherId:guid}/claim")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Claim(Guid voucherId, CancellationToken ct)
    {
        await Sender.Send(new ClaimVoucherCommand(voucherId), ct);
        return OkData<object?>(null, "Đã lưu voucher.");
    }

    [HttpGet("vouchers")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<WalletVoucherDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyVouchers([FromQuery] WalletTab tab = WalletTab.Valid, CancellationToken ct = default) =>
        OkData(await Sender.Send(new MyVouchersQuery(tab), ct));

    [HttpGet("coins")]
    [ProducesResponseType<ApiResponse<CoinWalletDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Coins([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new MyCoinsQuery(page, pageSize), ct));
}

[Route("api/seller/shops/{shopId:guid}/vouchers")]
[OwnerGuarded("Voucher của shop mà người gọi là nhân viên có quyền MARKETING.MANAGE; shop khác trả 404.")]
public sealed class SellerVouchersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<VoucherDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid shopId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        OkData(await Sender.Send(new ListShopVouchersQuery(shopId, page, pageSize), ct));

    [HttpPost]
    [ProducesResponseType<ApiResponse<VoucherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid shopId, [FromBody] VoucherInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveShopVoucherCommand(shopId, null, body), ct), "Đã tạo voucher.");

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<VoucherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid shopId, Guid id, [FromBody] VoucherInput body, CancellationToken ct) =>
        OkData(await Sender.Send(new SaveShopVoucherCommand(shopId, id, body), ct), "Đã lưu voucher.");

    [HttpPost("{id:guid}/stop")]
    [ProducesResponseType<ApiResponse<VoucherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop(Guid shopId, Guid id, CancellationToken ct) =>
        OkData(await Sender.Send(new StopShopVoucherCommand(shopId, id), ct), "Đã dừng voucher.");
}
