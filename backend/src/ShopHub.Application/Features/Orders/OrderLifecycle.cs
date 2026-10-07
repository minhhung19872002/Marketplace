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
        return await db.Orders.Include(o => o.Items).ThenInclude(i => i.Discounts).Include(o => o.History).Include(o => o.Packages)
            .AsSplitQuery().SingleAsync(o => o.Id == orderId, ct);
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
        if (order.PaymentMethod == PaymentMethod.Wallet)
        {
            // Back into Ví ShopHub: the finance sync credits the wallet and completes the refund (same order event)
            db.Refunds.Add(new Refund(order.Id, null, order.GrandTotal, RefundDestination.Wallet, reason, now));
            order.MarkRefunded();
            outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Refunded, null));
            return;
        }
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
    InventoryWriter inventory,
    IApplicationDbContext db,
    OrderLocks locks,
    OrderCanceller canceller,
    ICounterRecomputer counters,
    ISystemParameters parameters,
    Returns.ReturnRefunder refunder,
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
        else if (shipment.Direction == ShipmentDirection.Outbound
                 && await db.Shipments.CountAsync(s => s.OrderId == order.Id && s.Direction == ShipmentDirection.Outbound
                                                       && s.Status != ShipmentStatus.Cancelled, ct) > 1)
            touched = await ApplyParcelAsync(order, shipment, e, now, ct);
        else if (shipment.Direction == ShipmentDirection.Outbound)
        {
            switch (e.Status)
            {
                case ShipmentStatus.Picked when order.Status == OrderStatus.ReadyToShip:
                    OrderStateMachine.Transition(order, OrderStatus.Shipping, OrderActor.Carrier, null, "Đơn vị vận chuyển đã lấy hàng", now);
                    await TakeStockAsync(order, order.Items, now, ct);
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
                    await RestockAsync(order, order.Items, now, ct);
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

    /// <summary>
    /// An order in several parcels (đa kho): each parcel takes its own stock when picked up and puts it back when it comes
    /// home; the order moves only on the whole picture — "Đang giao" at the first pickup, and once every parcel has ended:
    /// all back → "Đã hoàn về" (everything refunded as for one parcel), some delivered → "Đã giao", and each parcel that
    /// came back is refunded on its own (its lines' paid share + its net shipping) as a system return.
    /// </summary>
    private async Task<bool> ApplyParcelAsync(Order order, Shipment shipment, CarrierEvent e, DateTimeOffset now, CancellationToken ct)
    {
        var items = order.Items.Where(i => i.PackageNo == shipment.PackageNo).ToList();
        var label = $"Kiện {shipment.PackageNo} ({shipment.TrackingNo})";
        var touched = false;
        switch (e.Status)
        {
            case ShipmentStatus.Picked when order.Status is OrderStatus.ReadyToShip or OrderStatus.Shipping:
                if (order.Status == OrderStatus.ReadyToShip)
                {
                    OrderStateMachine.Transition(order, OrderStatus.Shipping, OrderActor.Carrier, null, $"Đơn vị vận chuyển đã lấy {label}", now);
                    foreach (var open in await db.OrderCancelRequests.Where(r => r.OrderId == order.Id && r.Status == CancelRequestStatus.Pending).ToListAsync(ct))
                        open.Close("Đơn đã được giao cho đơn vị vận chuyển", now);
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Shipped, shipment.TrackingNo));
                }
                await TakeStockAsync(order, items, now, ct);
                touched = true;
                break;
            case ShipmentStatus.Failed when order.Status == OrderStatus.Shipping:
                outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.DeliveryFailed, $"{label}: {e.Description}"));
                break;
            case ShipmentStatus.Returned when order.Status == OrderStatus.Shipping:
                await RestockAsync(order, items, now, ct);
                touched = true;
                break;
        }
        if (e.Status is ShipmentStatus.Delivered or ShipmentStatus.Returned && order.Status == OrderStatus.Shipping)
        {
            var parcels = await db.Shipments.Where(s => s.OrderId == order.Id && s.Direction == ShipmentDirection.Outbound && s.Status != ShipmentStatus.Cancelled)
                .ToListAsync(ct);
            if (parcels.All(s => s.Status is ShipmentStatus.Delivered or ShipmentStatus.Returned))
            {
                if (parcels.All(s => s.Status == ShipmentStatus.Returned))
                {
                    OrderStateMachine.Transition(order, OrderStatus.DeliveryFailed, OrderActor.Carrier, null, "Không kiện nào giao được", now);
                    OrderStateMachine.Transition(order, OrderStatus.Returning, OrderActor.Carrier, null, "Các kiện đang hoàn về", now);
                    OrderStateMachine.Transition(order, OrderStatus.Returned, OrderActor.Carrier, null, "Mọi kiện đã hoàn về shop", now);
                    await canceller.GiveBackPromotionsAsync(order, now, ct);
                    await canceller.RefundIfPaidAsync(order, "Giao hàng không thành công, hàng đã hoàn về", now, ct);
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Returned, null));
                }
                else
                {
                    OrderStateMachine.Transition(order, OrderStatus.Delivered, OrderActor.Carrier, null, "Đã giao các kiện", now);
                    order.ScheduleAutoComplete(now.AddDays(await parameters.GetIntAsync(ParameterKeys.OrderAutoCompleteDays, ct)));
                    outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Delivered, null));
                    await db.SaveChangesAsync(ct);
                    foreach (var back in parcels.Where(s => s.Status == ShipmentStatus.Returned).OrderBy(s => s.PackageNo))
                        await RefundParcelAsync(order, back.PackageNo, now, ct);
                }
                touched = true;
            }
        }
        return touched;
    }

    /// <summary>A parcel that came back while the rest arrived: refunded at once as a system return (spec 3.8 amounts).</summary>
    private async Task RefundParcelAsync(Order order, int packageNo, DateTimeOffset now, CancellationToken ct)
    {
        var package = order.Packages.FirstOrDefault(p => p.No == packageNo);
        var r = ReturnRequest.ForUndeliveredParcel(order.Id, order.BuyerId, order.ShopId, Returns.CreateReturnHandler.NewCode(now), packageNo,
            package?.ShippingFee ?? 0, package?.ShippingDiscount ?? 0, now);
        long money = 0, coins = 0;
        foreach (var line in order.Items.Where(i => i.PackageNo == packageNo).OrderBy(i => i.Id))
        {
            var (m, c) = Returns.ReturnPricing.ForUnits(Returns.Returnability.PaidMoney(line), Returns.Returnability.PaidCoins(line), line.Quantity, 0,
                line.Quantity);
            r.Items.Add(new ReturnItem(r.Id, line.Id, line.Quantity, m, c));
            money += m;
            coins += c;
        }
        r.SetAmounts(money + r.ShippingRefund, coins);
        db.ReturnRequests.Add(r);
        await refunder.RefundAsync(r, money + r.ShippingRefund, coins, ReturnParty.System, r.Description, ct);
    }

    /// <summary>Handed to the carrier: the held quantity leaves the warehouse for real.</summary>
    private async Task TakeStockAsync(Order order, IEnumerable<OrderItem> items, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in items.OrderBy(i => i.SkuId))
        {
            var done = await inventory.TryMoveAsync(new InventoryMove(item.SkuId, -item.Quantity, -item.Quantity, InventoryReason.OrderShip, "order", order.Id,
                null, "Giao cho đơn vị vận chuyển"), ct);
            if (!done) throw new InvalidOperationException($"SKU {item.SkuId} holds less than order {order.Code} reserved.");
        }
    }

    private async Task RestockAsync(Order order, IEnumerable<OrderItem> items, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in items.OrderBy(i => i.SkuId))
        {
            await inventory.TryMoveAsync(new InventoryMove(item.SkuId, item.Quantity, 0, InventoryReason.ReturnRestock, "order", order.Id, null,
                "Hàng hoàn về kho"), ct);
        }
    }
}

/// <summary>Verifies a carrier's notification with its own rules, then applies it once.</summary>
public sealed class CarrierWebhookIntake(IEnumerable<ICarrier> carriers, ShipmentEventProcessor processor)
{
    public async Task<(bool Accepted, string Result)> HandleAsync(string provider, InboundWebhook webhook, CancellationToken ct)
    {
        var carrier = carriers.FirstOrDefault(c => string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase))
                      ?? throw new NotFoundException("Không tìm thấy đơn vị vận chuyển.");
        var e = carrier.VerifyWebhook(webhook);
        if (e is null) return (false, "INVALID_SIGNATURE");
        return (true, await processor.ApplyAsync(e, ct));
    }
}

/// <summary>
/// Recurring job (spec 3.7): completes delivered orders after N days, approves cancel requests the shop left
/// unanswered, and cancels orders a shop did not prepare in time (with a penalty point).
/// </summary>
public sealed class OrderAutomationService(
    IWorkingCalendar calendar,
    IApplicationDbContext db,
    OrderLocks locks,
    OrderCanceller canceller,
    ISystemParameters parameters,
    Seller.ShopPenaltyService penalties,
    IOutbox outbox,
    IClock clock,
    ILogger<OrderAutomationService> logger)
{
    public async Task<(int Completed, int AutoApproved, int Overdue)> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        await penalties.RecomputeExpiredAsync(ct);
        var penaltyDays = await parameters.GetIntAsync(ParameterKeys.ShopPenaltyExpiryDays, ct);
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
                    var deadline = await calendar.AddWorkingDaysAsync(start, days, ct);
                    if (VietnamTime.Today(now) <= deadline) return false;
                    await canceller.CancelAsync(order, OrderActor.System, null, "Shop không chuẩn bị hàng đúng hạn", ct);
                    db.ShopPenalties.Add(new ShopPenalty(order.ShopId, 1, "Không chuẩn bị hàng đúng hạn", order.Id, now, now.AddDays(penaltyDays)));
                    await penalties.RecomputeAsync(order.ShopId, ct);
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
