using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Finance;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Payments;

public static class WebhookResults
{
    public const string Paid = "PAID";
    public const string Failed = "FAILED";
    public const string Duplicate = "DUPLICATE";
    public const string AmountMismatch = "AMOUNT_MISMATCH";
    public const string UnknownPayment = "UNKNOWN_PAYMENT";
    public const string LateRefunded = "LATE_REFUNDED";
    public const string Ignored = "IGNORED";
}

/// <summary>
/// Applies a verified gateway notification exactly once (unique provider + event id). The return page is only for
/// display — this is the one place that marks money as received.
/// </summary>
public sealed class PaymentProcessor(
    IApplicationDbContext db,
    IPaymentGatewayRegistry gateways,
    TopupProcessor topups,
    IOutbox outbox,
    IClock clock,
    ILogger<PaymentProcessor> logger)
{
    public async Task<string> ProcessAsync(string provider, GatewayCallback callback, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent(provider, callback.EventId, callback.Raw, now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConflictException ce) when (ce.Constraint == "ux_webhook_event")
        {
            // Already handled (gateway retry / replay): acknowledge without doing anything twice
            await tx.RollbackAsync(ct);
            db.ClearTracking();
            return WebhookResults.Duplicate;
        }

        var evt = db.PaymentWebhookEvents.Local.Single(e => e.Provider == provider && e.EventId == callback.EventId);
        var result = await ApplyAsync(callback, now, ct);
        evt.Processed(result, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Payment webhook {Provider} event {EventId} for payment {PaymentId}: {Result}", provider, callback.EventId, callback.PaymentId, result);
        return result;
    }

    private async Task<string> ApplyAsync(GatewayCallback callback, DateTimeOffset now, CancellationToken ct)
    {
        await db.LockAsync($"payment:{callback.PaymentId}", ct);
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == callback.PaymentId, ct);
        if (payment is null) return WebhookResults.UnknownPayment;
        if (callback.Amount != payment.Amount) return WebhookResults.AmountMismatch;
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded) return WebhookResults.Ignored;

        if (payment.Purpose == PaymentPurpose.WalletTopup)
        {
            await db.LockAsync($"topup:{payment.CheckoutId}", ct);
            if (callback.Success) payment.Succeed(callback.ProviderTxnId ?? callback.EventId, callback.Raw, now);
            else payment.Fail(callback.ProviderTxnId, callback.FailureReason ?? "Thanh toán không thành công.", callback.Raw);
            await topups.ApplyAsync(payment, callback.Success, ct);
            return callback.Success ? WebhookResults.Paid : WebhookResults.Failed;
        }

        await db.LockAsync($"checkout:{payment.CheckoutId}", ct);
        var checkout = await db.CheckoutSessions.SingleAsync(c => c.Id == payment.CheckoutId, ct);

        if (!callback.Success)
        {
            // The buyer may try again ("Thanh toán lại") until the payment window closes
            payment.Fail(callback.ProviderTxnId, callback.FailureReason ?? "Thanh toán không thành công.", callback.Raw);
            // "Thanh toán thất bại" (spec VII): the buyer hears about it with the way back to "Thanh toán lại"
            if (checkout.Status == CheckoutStatus.AwaitingPayment)
                foreach (var orderId in await db.Orders.Where(o => o.CheckoutId == checkout.Id && o.Status == OrderStatus.PendingPayment).Select(o => o.Id)
                             .ToListAsync(ct))
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(orderId, OrderEvents.PaymentFailed,
                        callback.FailureReason ?? "giao dịch bị từ chối"));
            return WebhookResults.Failed;
        }

        payment.Succeed(callback.ProviderTxnId ?? callback.EventId, callback.Raw, now);
        if (checkout.Status == CheckoutStatus.AwaitingPayment)
        {
            checkout.MarkPaid(now);
            var orders = await db.Orders.Include(o => o.History).Where(o => o.CheckoutId == checkout.Id).ToListAsync(ct);
            foreach (var order in orders.Where(o => o.Status == OrderStatus.PendingPayment))
            {
                OrderStateMachine.Transition(order, OrderStatus.PendingConfirmation, OrderActor.Gateway, null, "Đã thanh toán", now);
                outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Paid, null));
            }
            // Any other open attempt for this checkout can no longer be used
            foreach (var other in await db.Payments.Where(p => p.CheckoutId == checkout.Id && p.Id != payment.Id).ToListAsync(ct)) other.Expire();
            return WebhookResults.Paid;
        }

        // Money arrived after the checkout had expired (stock already released): give it straight back
        var refunded = await gateways.For(payment.Method).RefundAsync(payment, payment.Amount, "Thanh toán sau khi đơn đã hết hạn", ct);
        if (refunded) payment.MarkRefunded(now);
        logger.LogWarning("Late payment {PaymentId} for expired checkout {CheckoutId}; refunded: {Refunded}", payment.Id, checkout.Id, refunded);
        return WebhookResults.LateRefunded;
    }
}

/// <summary>Gives back everything an unpaid checkout was holding: stock, voucher uses, xu.</summary>
public sealed class CheckoutReleaser(IApplicationDbContext db, VoucherLedger vouchers, Marketing.FlashSaleQuota flash, IOutbox outbox, IClock clock)
{
    /// <summary>Must run inside a transaction holding the checkout lock.</summary>
    public async Task ExpireAsync(CheckoutSession checkout, string reason, CancellationToken ct) =>
        await ExpireAsync(checkout, reason, OrderActor.System, null, ct);

    public async Task ExpireAsync(CheckoutSession checkout, string reason, OrderActor actor, Guid? actorId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        checkout.MarkExpired();
        foreach (var p in await db.Payments.Where(p => p.CheckoutId == checkout.Id).ToListAsync(ct)) p.Expire();

        var orders = await db.Orders.Include(o => o.Items).Include(o => o.History).Where(o => o.CheckoutId == checkout.Id).ToListAsync(ct);
        foreach (var order in orders.Where(o => o.Status == OrderStatus.PendingPayment))
        {
            OrderStateMachine.Transition(order, OrderStatus.Cancelled, actor, actorId, reason, now);
            await ReleaseStockAsync(order, now, ct);
            outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Cancelled, reason));
        }

        var usages = await db.VoucherUsages.Where(u => u.CheckoutId == checkout.Id).ToListAsync(ct);
        await vouchers.RevertAsync(usages, ct);

        if (checkout.CoinUsed > 0)
            db.CoinLedger.Add(new CoinEntry(checkout.UserId, checkout.CoinUsed, CoinReason.CheckoutRefund, "checkout", checkout.Id, null,
                "Hoàn xu do đơn hàng bị huỷ", now));

        outbox.Enqueue(OutboxTypes.SearchSyncProducts,
            new SearchSyncProductsPayload(orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList()));
    }

    /// <summary>Release the held quantity of every line (never below zero — the CHECK constraint is the backstop).</summary>
    public async Task ReleaseStockAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in order.Items.OrderBy(i => i.SkuId))
        {
            var released = await db.ExecuteSqlAsync(
                $"UPDATE catalog.skus SET reserved = reserved - {item.Quantity} WHERE id = {item.SkuId} AND reserved >= {item.Quantity}", ct);
            if (released == 0) throw new InvalidOperationException($"Reserved quantity of SKU {item.SkuId} is lower than order {order.Code} holds.");
            db.InventoryMovements.Add(new InventoryMovement(item.SkuId, 0, -item.Quantity, InventoryReason.OrderRelease, "order", order.Id, null,
                order.CancelReason, now));
        }
        // Flash Sale units bought by this order go back to the slot
        var flashLines = order.Items.Where(i => i.PriceSource is Domain.Promo.PriceProgramKind.ShopFlash or Domain.Promo.PriceProgramKind.PlatformFlash
                                                && i.PriceRefId is not null)
            .GroupBy(i => i.PriceRefId!.Value).Select(g => (g.Key, g.Sum(i => i.Quantity))).ToList();
        if (flashLines.Count > 0) await flash.ReleaseAsync(order.BuyerId, flashLines, ct);
    }
}

/// <summary>
/// Recurring job: for each checkout past its payment deadline, ask the gateway first (a payment may have succeeded
/// while its notification was lost); otherwise cancel the orders and release what they held.
/// </summary>
public sealed class PaymentExpiryService(
    IApplicationDbContext db,
    IPaymentGatewayRegistry gateways,
    PaymentProcessor processor,
    CheckoutReleaser releaser,
    IClock clock,
    ILogger<PaymentExpiryService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.CheckoutSessions.AsNoTracking()
            .Where(c => c.Status == CheckoutStatus.AwaitingPayment && c.PaymentExpiresAt <= now)
            .OrderBy(c => c.PaymentExpiresAt).ThenBy(c => c.Id).Select(c => c.Id).Take(200).ToListAsync(ct);

        var expired = 0;
        foreach (var checkoutId in due)
        {
            try
            {
                if (await ReconcileAsync(checkoutId, ct)) continue;
                await using var tx = await db.BeginTransactionAsync(ct);
                await db.LockAsync($"checkout:{checkoutId}", ct);
                var checkout = await db.CheckoutSessions.SingleAsync(c => c.Id == checkoutId, ct);
                if (checkout.Status != CheckoutStatus.AwaitingPayment)
                {
                    await tx.RollbackAsync(ct);
                    db.ClearTracking();
                    continue;
                }
                await releaser.ExpireAsync(checkout, "Quá hạn thanh toán", ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                db.ClearTracking();
                expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ClearTracking();
                logger.LogError(ex, "Could not expire checkout {CheckoutId}", checkoutId);
            }
        }
        if (expired > 0) logger.LogInformation("Expired {Count} unpaid checkout(s)", expired);
        await ExpireTopupsAsync(now, ct);
        return expired;
    }

    /// <summary>Wallet top-ups whose payment window passed: ask the gateway first, then give up on them.</summary>
    private async Task ExpireTopupsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Payments.AsNoTracking()
            .Where(p => p.Purpose == PaymentPurpose.WalletTopup && p.ExpiresAt <= now && (p.Status == PaymentStatus.Initiated || p.Status == PaymentStatus.Failed))
            .Where(p => db.WalletTopups.Any(t => t.Id == p.CheckoutId && t.Status == Domain.Finance.TopupStatus.Pending))
            .OrderBy(p => p.ExpiresAt).Take(200).ToListAsync(ct);
        foreach (var payment in due)
        {
            try
            {
                if (await ReconcileAsync(payment.CheckoutId, ct)) continue;
                await using var tx = await db.BeginTransactionAsync(ct);
                await db.LockAsync($"topup:{payment.CheckoutId}", ct);
                var topup = await db.WalletTopups.SingleAsync(t => t.Id == payment.CheckoutId, ct);
                topup.Expire(now);
                foreach (var p in await db.Payments.Where(x => x.CheckoutId == topup.Id).ToListAsync(ct)) p.Expire();
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                db.ClearTracking();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ClearTracking();
                logger.LogError(ex, "Could not expire wallet top-up {TopupId}", payment.CheckoutId);
            }
        }
    }

    /// <summary>True when the gateway says an attempt was actually paid (then it is applied like a notification).</summary>
    private async Task<bool> ReconcileAsync(Guid checkoutId, CancellationToken ct)
    {
        var open = await db.Payments.AsNoTracking().Where(p => p.CheckoutId == checkoutId && (p.Status == PaymentStatus.Initiated || p.Status == PaymentStatus.Failed))
            .ToListAsync(ct);
        foreach (var payment in open)
        {
            var gateway = gateways.For(payment.Method);
            var state = await gateway.QueryAsync(payment, ct);
            if (state.Status != GatewayTxnStatus.Succeeded) continue;
            var result = await processor.ProcessAsync(gateway.Provider,
                new GatewayCallback($"query:{payment.Id}", payment.Id, state.ProviderTxnId, state.Amount, true, null, state.Raw), ct);
            db.ClearTracking();
            if (result is WebhookResults.Paid or WebhookResults.Duplicate) return true;
        }
        return false;
    }
}
