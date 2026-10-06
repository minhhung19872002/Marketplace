using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Checkout;

public record QuoteCheckoutQuery(CheckoutRequest Request) : IRequest<CheckoutQuoteDto>;

public sealed class QuoteCheckoutHandler(CheckoutBuilder builder, ICurrentUser currentUser) : IRequestHandler<QuoteCheckoutQuery, CheckoutQuoteDto>
{
    public async Task<CheckoutQuoteDto> Handle(QuoteCheckoutQuery request, CancellationToken ct) =>
        (await builder.BuildAsync(UserGuard.Require(currentUser), request.Request, ct)).Quote;
}

public record PlacedOrderDto(Guid Id, string Code, Guid ShopId, string ShopName, OrderStatus Status, long GrandTotal);

public record CheckoutPaymentDto(Guid PaymentId, PaymentMethod Method, PaymentStatus Status, long Amount, string? RedirectUrl, DateTimeOffset ExpiresAt);

public record CheckoutResultDto(
    Guid CheckoutId,
    CheckoutStatus Status,
    PaymentMethod PaymentMethod,
    long GrandTotal,
    DateTimeOffset? PaymentExpiresAt,
    IReadOnlyList<PlacedOrderDto> Orders,
    CheckoutPaymentDto? Payment);

/// <param name="ExpectedGrandTotal">What the buyer saw; a different server total is a 409 with the new quote.</param>
public record PlaceOrderCommand(string IdempotencyKey, CheckoutRequest Request, long ExpectedGrandTotal) : IRequest<CheckoutResultDto>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithMessage("Thiếu Idempotency-Key.")
            .MaximumLength(100).WithMessage("Idempotency-Key tối đa 100 ký tự.");
        RuleFor(x => x.ExpectedGrandTotal).GreaterThanOrEqualTo(0).WithMessage("Tổng thanh toán không hợp lệ.");
        RuleForEach(x => x.Request.Shops).ChildRules(s =>
            s.RuleFor(c => c.Note).MaximumLength(200).WithMessage("Lời nhắn cho shop tối đa 200 ký tự."));
    }
}

/// <summary>
/// Places one checkout = one order per shop, in a single transaction (spec 3.5):
/// idempotency row first (unique key), then xu (per-user lock), stock holds (conditional UPDATE, SKUs in id order so
/// parallel checkouts cannot deadlock), voucher uses (conditional UPDATE), orders with per-line discount allocation,
/// and finally the payment attempt for online methods.
/// </summary>
public sealed class PlaceOrderHandler(
    IApplicationDbContext db,
    CheckoutBuilder builder,
    VoucherLedger voucherLedger,
    CoinWallet coins,
    CheckoutReader reader,
    PaymentStarter payments,
    IOutbox outbox,
    ISystemParameters parameters,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<PlaceOrderCommand, CheckoutResultDto>
{
    public async Task<CheckoutResultDto> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var key = request.IdempotencyKey.Trim();

        // A retry of a request that already went through gets the same answer
        if (await reader.FindByKeyAsync(userId, key, ct) is { } existing) return existing;

        var plan = await builder.BuildAsync(userId, request.Request, ct);
        if (!plan.Quote.CanPlace || plan.Pricing is null)
            throw new ConflictException(plan.Quote.Problems.FirstOrDefault() ?? "Không thể đặt hàng, vui lòng kiểm tra lại.", "CHECKOUT_INVALID")
                { Payload = plan.Quote };
        if (plan.Quote.GrandTotal != request.ExpectedGrandTotal)
            throw new ConflictException("Giá hoặc ưu đãi vừa thay đổi. Vui lòng kiểm tra lại tổng thanh toán trước khi đặt hàng.", "PRICE_CHANGED")
                { Payload = plan.Quote };

        var now = clock.UtcNow;
        var method = request.Request.PaymentMethod;
        var timeout = await parameters.GetIntAsync(ParameterKeys.PaymentTimeoutMinutes, ct);
        var pricing = plan.Pricing;
        CheckoutSession checkout;
        var orders = new List<Order>();

        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            checkout = new CheckoutSession(userId, key, plan.AddressSnapshot, method, now);
            db.CheckoutSessions.Add(checkout);
            try
            {
                // Takes the unique (user, key) slot now: a parallel twin waits here, then sees the conflict
                await db.SaveChangesAsync(ct);
            }
            catch (ConflictException ce) when (ce.Constraint == "ux_checkout_idem")
            {
                await tx.RollbackAsync(ct);
                db.ClearTracking();
                return await reader.FindByKeyAsync(userId, key, ct)
                       ?? throw new ConflictException("Đơn hàng đang được xử lý, vui lòng thử lại sau giây lát.", "CHECKOUT_IN_PROGRESS");
            }

            // ----- xu -----
            if (pricing.CoinUsed > 0)
            {
                var balance = await coins.LockedBalanceAsync(userId, ct);
                if (balance < pricing.CoinUsed)
                    throw new ConflictException("Số xu không đủ, vui lòng tải lại trang thanh toán.", "COINS_CHANGED");
                db.CoinLedger.Add(new CoinEntry(userId, -pricing.CoinUsed, CoinReason.CheckoutSpend, "checkout", checkout.Id, null, "Dùng xu khi đặt hàng", now));
            }

            // ----- stock holds -----
            foreach (var line in plan.Lines.Values.OrderBy(l => l.SkuId))
            {
                var held = await db.ExecuteSqlAsync($"""
                    UPDATE catalog.skus SET reserved = reserved + {line.Quantity}
                    WHERE id = {line.SkuId} AND is_active AND stock - reserved >= {line.Quantity}
                    """, ct);
                if (held == 0)
                {
                    var left = await db.Skus.Where(s => s.Id == line.SkuId).Select(s => s.Stock - s.Reserved).FirstOrDefaultAsync(ct);
                    throw new ConflictException(left <= 0 ? $"\"{line.Name}\" vừa hết hàng." : $"\"{line.Name}\" chỉ còn {left} sản phẩm.", "OUT_OF_STOCK");
                }
                db.InventoryMovements.Add(new InventoryMovement(line.SkuId, 0, line.Quantity, InventoryReason.OrderReserve, "checkout", checkout.Id,
                    userId, null, now));
            }

            // ----- orders (one per shop) -----
            var choices = (request.Request.Shops ?? []).GroupBy(c => c.ShopId).ToDictionary(g => g.Key, g => g.First());
            foreach (var shop in pricing.Shops)
            {
                var carrier = plan.Carriers[shop.ShopId];
                var order = new Order(checkout.Id, userId, shop.ShopId, await NewCodeAsync(ct), method, carrier.Code,
                    string.IsNullOrWhiteSpace(choices.GetValueOrDefault(shop.ShopId)?.Note) ? null : choices[shop.ShopId].Note!.Trim(), now);
                order.SetTotals(shop.Subtotal, shop.ShopDiscount, shop.PlatformDiscount, shop.ShippingFee, shop.ShippingDiscount, shop.CoinUsed,
                    shop.ShopVoucherId, carrier.Days);
                foreach (var pl in shop.Lines)
                {
                    var info = plan.Lines[pl.Line.SkuId];
                    var item = new OrderItem(order.Id, info.SkuId, info.ProductId, info.Name, info.Variant, info.ImageUrl, info.UnitPrice,
                        info.OriginalPrice, info.Quantity);
                    if (pl.ShopDiscount > 0) item.Discounts.Add(new OrderItemDiscount(item.Id, DiscountSource.Shop, shop.ShopVoucherId, pl.ShopDiscount));
                    if (pl.PlatformDiscount > 0)
                        item.Discounts.Add(new OrderItemDiscount(item.Id, DiscountSource.Platform, plan.PlatformVoucher?.Id, pl.PlatformDiscount));
                    if (pl.CoinDiscount > 0) item.Discounts.Add(new OrderItemDiscount(item.Id, DiscountSource.Coin, null, pl.CoinDiscount));
                    order.Items.Add(item);
                }
                OrderStateMachine.Start(order, OrderActor.Buyer, userId, now);
                db.Orders.Add(order);
                orders.Add(order);
            }

            // ----- voucher uses (fixed order: shop vouchers by id, then platform ones) -----
            foreach (var (shopId, v) in plan.ShopVouchers.OrderBy(kv => kv.Value.Id))
            {
                var order = orders.Single(o => o.ShopId == shopId);
                await voucherLedger.ConsumeAsync(v, userId, checkout.Id, order.Id, order.ShopDiscount, ct);
            }
            foreach (var v in new[] { plan.PlatformVoucher, plan.FreeshipVoucher }.Where(v => v is not null).OrderBy(v => v!.Id))
            {
                var amount = v!.Type == VoucherType.FreeShipping ? pricing.ShippingDiscount
                    : v.Type == VoucherType.CoinCashback ? pricing.CoinCashback : pricing.PlatformDiscount;
                await voucherLedger.ConsumeAsync(v, userId, checkout.Id, null, amount, ct);
            }

            var expiresAt = method == PaymentMethod.Cod ? (DateTimeOffset?)null : now.AddMinutes(timeout);
            checkout.SetTotals(pricing.Subtotal, pricing.ShippingFee, pricing.ShippingDiscount, pricing.ShopDiscount + pricing.PlatformDiscount,
                pricing.CoinUsed, pricing.GrandTotal, plan.PlatformVoucher?.Id, plan.FreeshipVoucher?.Id, expiresAt);

            // ----- payment attempt -----
            if (method != PaymentMethod.Cod) await payments.StartAsync(checkout, orders.Select(o => o.Code), ct);

            // ----- the bought lines leave the cart -----
            var bought = plan.Lines.Keys.ToList();
            var cart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);
            cart?.Remove(bought, now);

            // Stock holds were set-based: refresh "còn hàng" in the search index
            outbox.Enqueue(OutboxTypes.SearchSyncProducts, new SearchSyncProductsPayload(plan.Lines.Values.Select(l => l.ProductId).Distinct().ToList()));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return await reader.FindByKeyAsync(userId, key, ct) ?? throw new InvalidOperationException("Checkout vanished after commit.");
    }

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>"SH" + Vietnam date + 6 random characters (no 0/O/1/I), unique index as the backstop.</summary>
    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var random = string.Create(6, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            });
            var code = $"SH{VietnamTime.ToLocal(clock.UtcNow):yyMMdd}{random}";
            if (!await db.Orders.AnyAsync(o => o.Code == code, ct)) return code;
        }
        throw new ConflictException("Không tạo được mã đơn, vui lòng thử lại.");
    }
}

/// <summary>Reads a checkout with its orders and current payment attempt (only the caller's own).</summary>
public sealed class CheckoutReader(IApplicationDbContext db)
{
    public async Task<CheckoutResultDto?> FindByKeyAsync(Guid userId, string key, CancellationToken ct)
    {
        var id = await db.CheckoutSessions.AsNoTracking().Where(c => c.UserId == userId && c.IdempotencyKey == key).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await GetAsync(userId, id.Value, ct);
    }

    public async Task<CheckoutResultDto?> GetAsync(Guid userId, Guid checkoutId, CancellationToken ct)
    {
        var checkout = await db.CheckoutSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkoutId && c.UserId == userId, ct);
        if (checkout is null) return null;
        var orders = await (from o in db.Orders.AsNoTracking()
                            join s in db.Shops.AsNoTracking() on o.ShopId equals s.Id
                            where o.CheckoutId == checkout.Id
                            orderby o.Code
                            select new PlacedOrderDto(o.Id, o.Code, o.ShopId, s.Name, o.Status, o.GrandTotal)).ToListAsync(ct);
        var payment = await db.Payments.AsNoTracking().Where(p => p.CheckoutId == checkout.Id)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        CheckoutPaymentDto? paymentDto = null;
        if (payment is not null)
        {
            var redirect = payment.Status == PaymentStatus.Initiated && checkout.Status == CheckoutStatus.AwaitingPayment ? payment.RedirectUrl : null;
            paymentDto = new CheckoutPaymentDto(payment.Id, payment.Method, payment.Status, payment.Amount, redirect, payment.ExpiresAt);
        }
        return new CheckoutResultDto(checkout.Id, checkout.Status, checkout.PaymentMethod, checkout.GrandTotal, checkout.PaymentExpiresAt, orders, paymentDto);
    }
}

public record GetCheckoutQuery(Guid CheckoutId) : IRequest<CheckoutResultDto>;

public sealed class GetCheckoutHandler(CheckoutReader reader, ICurrentUser currentUser) : IRequestHandler<GetCheckoutQuery, CheckoutResultDto>
{
    public async Task<CheckoutResultDto> Handle(GetCheckoutQuery request, CancellationToken ct) =>
        await reader.GetAsync(UserGuard.Require(currentUser), request.CheckoutId, ct) ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
}

/// <summary>"Thanh toán lại": a new attempt after a failure, while the payment window is still open.</summary>
public record RetryPaymentCommand(Guid CheckoutId) : IRequest<CheckoutResultDto>;

public sealed class RetryPaymentHandler(IApplicationDbContext db, CheckoutReader reader, PaymentStarter payments, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<RetryPaymentCommand, CheckoutResultDto>
{
    public async Task<CheckoutResultDto> Handle(RetryPaymentCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var now = clock.UtcNow;
        await db.InLockedTransactionAsync($"checkout:{request.CheckoutId}", async () =>
        {
            var checkout = await db.CheckoutSessions.FirstOrDefaultAsync(c => c.Id == request.CheckoutId && c.UserId == userId, ct)
                           ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
            if (checkout.Status != CheckoutStatus.AwaitingPayment) throw new ConflictException("Đơn hàng không còn chờ thanh toán.", "NOT_AWAITING");
            if (checkout.PaymentExpiresAt <= now) throw new ConflictException("Đã quá hạn thanh toán, đơn hàng sẽ được huỷ.", "PAYMENT_EXPIRED");
            var open = await db.Payments.Where(p => p.CheckoutId == checkout.Id && p.Status == PaymentStatus.Initiated).AnyAsync(ct);
            if (!open)
            {
                var codes = await db.Orders.Where(o => o.CheckoutId == checkout.Id).OrderBy(o => o.Code).Select(o => o.Code).ToListAsync(ct);
                await payments.StartAsync(checkout, codes, ct);
            }
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await reader.GetAsync(userId, request.CheckoutId, ct) ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
    }
}

/// <summary>Creates a payment attempt at the gateway for the checkout's amount, valid until the checkout's deadline.</summary>
public sealed class PaymentStarter(IApplicationDbContext db, IPaymentGatewayRegistry gateways, IClock clock)
{
    public async Task<Payment> StartAsync(CheckoutSession checkout, IEnumerable<string> orderCodes, CancellationToken ct)
    {
        var payment = new Payment(checkout.Id, checkout.PaymentMethod, checkout.GrandTotal, checkout.PaymentExpiresAt!.Value, clock.UtcNow);
        db.Payments.Add(payment);
        var start = await gateways.For(payment.Method).CreatePaymentAsync(new GatewayPaymentRequest(payment.Id, checkout.Id, payment.Amount,
            $"Thanh toán đơn hàng {string.Join(", ", orderCodes)}", payment.ExpiresAt), ct);
        payment.SetRedirect(start.RedirectUrl);
        return payment;
    }
}
