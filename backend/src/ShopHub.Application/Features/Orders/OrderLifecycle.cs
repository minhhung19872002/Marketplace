using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Payments;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Common;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Orders;

/// <summary>Loads an order for a state change, serialised per order (advisory lock) inside the caller's transaction.</summary>
public sealed class OrderLocks(IApplicationDbContext db)
{
    public async Task<Order> LockAsync(Guid orderId, CancellationToken ct)
    {
        await db.LockAsync($"order:{orderId}", ct);
        return await db.Orders.Include(o => o.Items).Include(o => o.History).SingleAsync(o => o.Id == orderId, ct);
    }
}

/// <summary>
/// Cancels an order that has not been handed to the carrier yet and gives back everything it took: held stock,
/// the shop voucher (and the platform ones once every order of the checkout is cancelled), xu, and the money
/// when it was paid online. Must run inside a transaction holding the order's lock.
/// </summary>
public sealed class OrderCanceller(
    IApplicationDbContext db,
    CheckoutReleaser releaser,
    VoucherLedger vouchers,
    IPaymentGatewayRegistry gateways,
    IEnumerable<ICarrier> carriers,
    IOutbox outbox,
    IClock clock)
{
    public async Task CancelAsync(Order order, OrderActor actor, Guid? actorId, string reason, CancellationToken ct)
    {
        if (order.Status is not (OrderStatus.PendingConfirmation or OrderStatus.ReadyToShip))
            throw new BusinessRuleException($"Không thể huỷ đơn ở trạng thái \"{OrderStateMachine.Label(order.Status)}\".");
        var now = clock.UtcNow;
        OrderStateMachine.Transition(order, OrderStatus.Cancelled, actor, actorId, reason, now);
        await releaser.ReleaseStockAsync(order, now, ct);

        // A booked pickup is cancelled at the carrier too
        foreach (var shipment in await db.Shipments.Include(s => s.Events)
                     .Where(s => s.OrderId == order.Id && s.Direction == ShipmentDirection.Outbound && s.Status == ShipmentStatus.Created).ToListAsync(ct))
        {
            var carrier = await db.Carriers.AsNoTracking().SingleAsync(c => c.Code == shipment.CarrierCode, ct);
            var provider = carriers.FirstOrDefault(c => c.Provider == carrier.Provider);
            if (provider is not null) await provider.CancelShipmentAsync(carrier, shipment.TrackingNo, ct);
            shipment.Cancel(now);
        }
        foreach (var open in await db.OrderCancelRequests.Where(r => r.OrderId == order.Id && r.Status == CancelRequestStatus.Pending).ToListAsync(ct))
            open.Close("Đơn đã được huỷ", now);

        await GiveBackPromotionsAsync(order, now, ct);
        await RefundIfPaidAsync(order, reason, now, ct);

        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Cancelled, reason));
        outbox.Enqueue(OutboxTypes.SearchSyncProducts, new SearchSyncProductsPayload(order.Items.Select(i => i.ProductId).Distinct().ToList()));
    }

    /// <summary>Shop voucher of this order; platform vouchers only once the whole checkout is cancelled; xu of this order.</summary>
    public async Task GiveBackPromotionsAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        var usages = await db.VoucherUsages.Where(u => u.CheckoutId == order.CheckoutId && u.RevertedAt == null).ToListAsync(ct);
        var others = await db.Orders.Where(o => o.CheckoutId == order.CheckoutId && o.Id != order.Id)
            .AnyAsync(o => o.Status != OrderStatus.Cancelled, ct);
        var giveBack = usages.Where(u => u.OrderId == order.Id || (u.OrderId == null && !others)).ToList();
        await vouchers.RevertAsync(giveBack, ct);

        if (order.CoinUsed > 0)
            db.CoinLedger.Add(new CoinEntry(order.BuyerId, order.CoinUsed, CoinReason.CheckoutRefund, "order", order.Id,
                null, $"Hoàn xu của đơn {order.Code}", now));
    }

    /// <summary>Paid online → refund this order's amount to the payment source (partial refund of the checkout's payment).</summary>
    public async Task RefundIfPaidAsync(Order order, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (order.PaymentStatus != OrderPaymentStatus.Paid || order.PaymentMethod == PaymentMethod.Cod || order.GrandTotal == 0) return;
        var payment = await db.Payments.Where(p => p.CheckoutId == order.CheckoutId && p.Status == PaymentStatus.Succeeded)
            .OrderByDescending(p => p.PaidAt).FirstOrDefaultAsync(ct);
        var refund = new Refund(order.Id, payment?.Id, order.GrandTotal, RefundDestination.Gateway, reason, now);
        db.Refunds.Add(refund);
        var ok = payment is not null && await gateways.For(payment.Method).RefundAsync(payment, order.GrandTotal, reason, ct);
        refund.Complete(ok, payment?.ProviderTxnId, now);
        if (!ok) return;
        order.MarkRefunded();
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Refunded, null));
    }
}

/// <summary>
/// Applies carrier status events to the shipment and moves the order with it (spec 3.3, 3.7): picked up → the stock
/// is really taken (stock − n, reserved − n); delivered → the auto-complete clock starts; returned → back into stock
/// and the money goes back. Replayed or out-of-order events are ignored.
/// </summary>
public sealed class ShipmentEventProcessor(
    IApplicationDbContext db,
    OrderLocks locks,
    OrderCanceller canceller,
    ICounterRecomputer counters,
    ISystemParameters parameters,
    IOutbox outbox,
    IClock clock,
    ILogger<ShipmentEventProcessor> logger)
{
    public async Task<string> ApplyAsync(CarrierEvent e, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"shipment:{e.TrackingNo}", ct);
        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.TrackingNo == e.TrackingNo, ct);
        if (shipment is null) return "UNKNOWN_SHIPMENT";
        if (!shipment.Apply(e.Status, e.Location, e.Description, e.EventId, e.OccurredAt, e.Raw)) return "IGNORED";

        var order = await locks.LockAsync(shipment.OrderId, ct);
        var now = clock.UtcNow;
        var touched = false;
        if (shipment.Direction == ShipmentDirection.Return && shipment.ReturnId is { } returnId)
            await Returns.ReturnAutomationService.OnReturnParcelAsync(db, returnId, e.Status, parameters, now, ct);
        else if (shipment.Direction == ShipmentDirection.Outbound)
        {
            switch (e.Status)
            {
                case ShipmentStatus.Picked when order.Status == OrderStatus.ReadyToShip:
                    OrderStateMachine.Transition(order, OrderStatus.Shipping, OrderActor.Carrier, null, "Đơn vị vận chuyển đã lấy hàng", now);
                    await TakeStockAsync(order, now, ct);
                    foreach (var open in await db.OrderCancelRequests.Where(r => r.OrderId == order.Id && r.Status == CancelRequestStatus.Pending).ToListAsync(ct))
                        open.Close("Đơn đã được giao cho đơn vị vận chuyển", now);
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Shipped, shipment.TrackingNo));
                    touched = true;
                    break;
                case ShipmentStatus.Delivered when order.Status == OrderStatus.Shipping:
                    OrderStateMachine.Transition(order, OrderStatus.Delivered, OrderActor.Carrier, null, "Giao hàng thành công", now);
                    order.ScheduleAutoComplete(now.AddDays(await parameters.GetIntAsync(ParameterKeys.OrderAutoCompleteDays, ct)));
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Delivered, null));
                    break;
                case ShipmentStatus.Failed when order.Status == OrderStatus.Shipping:
                    OrderStateMachine.Transition(order, OrderStatus.DeliveryFailed, OrderActor.Carrier, null, e.Description, now);
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.DeliveryFailed, e.Description));
                    break;
                case ShipmentStatus.OutForDelivery when order.Status == OrderStatus.DeliveryFailed:
                    OrderStateMachine.Transition(order, OrderStatus.Shipping, OrderActor.Carrier, null, "Giao lại", now);
                    break;
                case ShipmentStatus.Returning when order.Status == OrderStatus.DeliveryFailed:
                    OrderStateMachine.Transition(order, OrderStatus.Returning, OrderActor.Carrier, null, "Đang hoàn hàng về người gửi", now);
                    break;
                case ShipmentStatus.Returned when order.Status == OrderStatus.Returning:
                    OrderStateMachine.Transition(order, OrderStatus.Returned, OrderActor.Carrier, null, "Đã hoàn hàng về shop", now);
                    await RestockAsync(order, now, ct);
                    await canceller.GiveBackPromotionsAsync(order, now, ct);
                    await canceller.RefundIfPaidAsync(order, "Giao hàng không thành công, hàng đã hoàn về", now, ct);
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Returned, null));
                    touched = true;
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        if (touched)
        {
            await counters.RecomputeProductSalesAsync(order.Items.Select(i => i.ProductId).Distinct().ToList(), ct);
            outbox.Enqueue(OutboxTypes.SearchSyncProducts, new SearchSyncProductsPayload(order.Items.Select(i => i.ProductId).Distinct().ToList()));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        logger.LogInformation("Shipment {TrackingNo} → {Status}; order {OrderCode} is {OrderStatus}", e.TrackingNo, e.Status, order.Code, order.Status);
        return "APPLIED";
    }

    /// <summary>Handed to the carrier: the held quantity leaves the warehouse for real.</summary>
    private async Task TakeStockAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in order.Items.OrderBy(i => i.SkuId))
        {
            var done = await db.ExecuteSqlAsync($"""
                UPDATE catalog.skus SET stock = stock - {item.Quantity}, reserved = reserved - {item.Quantity}
                WHERE id = {item.SkuId} AND reserved >= {item.Quantity} AND stock >= {item.Quantity}
                """, ct);
            if (done == 0) throw new InvalidOperationException($"SKU {item.SkuId} holds less than order {order.Code} reserved.");
            db.InventoryMovements.Add(new InventoryMovement(item.SkuId, -item.Quantity, -item.Quantity, InventoryReason.OrderShip, "order", order.Id, null,
                "Giao cho đơn vị vận chuyển", now));
        }
    }

    private async Task RestockAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in order.Items.OrderBy(i => i.SkuId))
        {
            await db.ExecuteSqlAsync($"UPDATE catalog.skus SET stock = stock + {item.Quantity} WHERE id = {item.SkuId}", ct);
            db.InventoryMovements.Add(new InventoryMovement(item.SkuId, item.Quantity, 0, InventoryReason.ReturnRestock, "order", order.Id, null,
                "Hàng hoàn về kho", now));
        }
    }
}

/// <summary>Verifies a carrier's notification with its own rules, then applies it once.</summary>
public sealed class CarrierWebhookIntake(IEnumerable<ICarrier> carriers, ShipmentEventProcessor processor)
{
    public async Task<(bool Accepted, string Result)> HandleAsync(string provider, IReadOnlyDictionary<string, string> headers, string body,
        CancellationToken ct)
    {
        var carrier = carriers.FirstOrDefault(c => string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase))
                      ?? throw new NotFoundException("Không tìm thấy đơn vị vận chuyển.");
        var e = carrier.VerifyWebhook(headers, body);
        if (e is null) return (false, "INVALID_SIGNATURE");
        return (true, await processor.ApplyAsync(e, ct));
    }
}

/// <summary>
/// Recurring job (spec 3.7): completes delivered orders after N days, approves cancel requests the shop left
/// unanswered, and cancels orders a shop did not prepare in time (with a penalty point).
/// </summary>
public sealed class OrderAutomationService(
    IApplicationDbContext db,
    OrderLocks locks,
    OrderCanceller canceller,
    ISystemParameters parameters,
    IOutbox outbox,
    IClock clock,
    ILogger<OrderAutomationService> logger)
{
    public async Task<(int Completed, int AutoApproved, int Overdue)> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var completed = 0;
        foreach (var id in await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Delivered && o.AutoCompleteAt <= now)
                     .OrderBy(o => o.AutoCompleteAt).ThenBy(o => o.Id).Select(o => o.Id).Take(500).ToListAsync(ct))
        {
            if (await OneAsync(id, order =>
                {
                    if (order.Status != OrderStatus.Delivered) return Task.FromResult(false);
                    OrderStateMachine.Transition(order, OrderStatus.Completed, OrderActor.System, null, "Tự động hoàn thành", now);
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Completed, null));
                    return Task.FromResult(true);
                }, ct)) completed++;
        }

        var approved = 0;
        foreach (var request in await db.OrderCancelRequests.AsNoTracking().Where(r => r.Status == CancelRequestStatus.Pending && r.DueAt <= now)
                     .OrderBy(r => r.DueAt).ThenBy(r => r.Id).Take(500).ToListAsync(ct))
        {
            if (await OneAsync(request.OrderId, async order =>
                {
                    var open = await db.OrderCancelRequests.SingleAsync(r => r.Id == request.Id, ct);
                    if (open.Status != CancelRequestStatus.Pending) return false;
                    open.Approve(null, automatic: true, now);
                    await canceller.CancelAsync(order, OrderActor.System, null, $"Shop không phản hồi yêu cầu huỷ: {open.Reason}", ct);
                    return true;
                }, ct)) approved++;
        }

        var overdue = 0;
        var days = (int)await parameters.GetIntAsync(ParameterKeys.OrderShipDeadlineDays, ct);
        // Generous pre-filter in SQL; the exact working-day deadline is checked per order
        var horizon = now.AddDays(-days);
        foreach (var id in await db.Orders.AsNoTracking()
                     .Where(o => (o.Status == OrderStatus.PendingConfirmation || o.Status == OrderStatus.ReadyToShip) && (o.PaidAt ?? o.CreatedAt) <= horizon)
                     .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Select(o => o.Id).Take(500).ToListAsync(ct))
        {
            if (await OneAsync(id, async order =>
                {
                    if (order.Status is not (OrderStatus.PendingConfirmation or OrderStatus.ReadyToShip)) return false;
                    var start = VietnamTime.Today(order.PaidAt ?? order.CreatedAt);
                    var deadline = VietnamTime.AddWorkingDays(start, days, new HashSet<DateOnly>());
                    if (VietnamTime.Today(now) <= deadline) return false;
                    await canceller.CancelAsync(order, OrderActor.System, null, "Shop không chuẩn bị hàng đúng hạn", ct);
                    db.ShopPenalties.Add(new ShopPenalty(order.ShopId, 1, "Không chuẩn bị hàng đúng hạn", order.Id, now));
                    await db.SaveChangesAsync(ct);
                    await db.ExecuteSqlAsync($"""
                        UPDATE shop.shops SET penalty_points = (SELECT COALESCE(SUM(points), 0) FROM shop.shop_penalties WHERE shop_id = {order.ShopId})
                        WHERE id = {order.ShopId}
                        """, ct);
                    return true;
                }, ct)) overdue++;
        }

        if (completed + approved + overdue > 0)
            logger.LogInformation("Order automation: {Completed} completed, {Approved} cancel request(s) auto-approved, {Overdue} overdue cancelled",
                completed, approved, overdue);
        return (completed, approved, overdue);
    }

    /// <summary>Each order in its own transaction: one failure never blocks the rest.</summary>
    private async Task<bool> OneAsync(Guid orderId, Func<Order, Task<bool>> work, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            var order = await locks.LockAsync(orderId, ct);
            if (!await work(order))
            {
                await tx.RollbackAsync(ct);
                db.ClearTracking();
                return false;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ClearTracking();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ClearTracking();
            logger.LogError(ex, "Order automation failed for order {OrderId}", orderId);
            return false;
        }
    }
}
