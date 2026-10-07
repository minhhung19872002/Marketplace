using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Cart;
using ShopHub.Application.Features.Payments;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Orders;

internal static class BuyerOrders
{
    /// <summary>The caller's own order id by code — anyone else's is "not found".</summary>
    public static async Task<Guid> OwnIdAsync(IApplicationDbContext db, ICurrentUser user, string code, CancellationToken ct)
    {
        var userId = UserGuard.Require(user);
        var normalized = code.Trim().ToUpperInvariant();
        return await db.Orders.Where(o => o.Code == normalized && o.BuyerId == userId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
    }
}

public sealed class OrderReasonValidator<T> : AbstractValidator<T> where T : IHasReason
{
    public OrderReasonValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Vui lòng chọn lý do.").MaximumLength(300).WithMessage("Lý do tối đa 300 ký tự.");
}

public interface IHasReason
{
    string Reason { get; }
}

/// <summary>
/// "Huỷ đơn": before the shop confirmed, the buyer cancels directly. An unpaid online order cancels its whole checkout
/// (one payment covers every shop's order). After confirmation the buyer must send a cancel request instead.
/// </summary>
public record CancelMyOrderCommand(string Code, string Reason) : IRequest<Unit>, IHasReason;

public sealed class CancelMyOrderValidator : AbstractValidator<CancelMyOrderCommand>
{
    public CancelMyOrderValidator() => Include(new OrderReasonValidator<CancelMyOrderCommand>());
}

public sealed class CancelMyOrderHandler(
    IApplicationDbContext db,
    OrderLocks locks,
    OrderCanceller canceller,
    CheckoutReleaser releaser,
    ICurrentUser currentUser) : IRequestHandler<CancelMyOrderCommand, Unit>
{
    public async Task<Unit> Handle(CancelMyOrderCommand request, CancellationToken ct)
    {
        var orderId = await BuyerOrders.OwnIdAsync(db, currentUser, request.Code, ct);
        var reason = request.Reason.Trim();
        await using var tx = await db.BeginTransactionAsync(ct);
        var checkoutId = await db.Orders.Where(o => o.Id == orderId).Select(o => o.CheckoutId).SingleAsync(ct);
        await db.LockAsync($"checkout:{checkoutId}", ct);
        var order = await locks.LockAsync(orderId, ct);
        switch (order.Status)
        {
            case OrderStatus.PendingPayment:
                var checkout = await db.CheckoutSessions.SingleAsync(c => c.Id == checkoutId, ct);
                await releaser.ExpireAsync(checkout, $"Người mua huỷ: {reason}", OrderActor.Buyer, currentUser.UserId, ct);
                break;
            case OrderStatus.PendingConfirmation:
                await canceller.CancelAsync(order, OrderActor.Buyer, currentUser.UserId, $"Người mua huỷ: {reason}", ct);
                break;
            case OrderStatus.ReadyToShip:
                throw new ConflictException("Shop đã xác nhận đơn, vui lòng gửi yêu cầu huỷ để shop xem xét.", "NEEDS_REQUEST");
            default:
                throw new ConflictException($"Không thể huỷ đơn ở trạng thái \"{OrderStateMachine.Label(order.Status)}\".", "CANNOT_CANCEL");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

/// <summary>"Yêu cầu huỷ" after the shop confirmed: the shop answers within ORDER.CANCEL_REQUEST_HOURS, otherwise it is approved.</summary>
public record RequestCancelCommand(string Code, string Reason) : IRequest<Unit>, IHasReason;

public sealed class RequestCancelValidator : AbstractValidator<RequestCancelCommand>
{
    public RequestCancelValidator() => Include(new OrderReasonValidator<RequestCancelCommand>());
}

public sealed class RequestCancelHandler(
    IApplicationDbContext db,
    OrderLocks locks,
    ISystemParameters parameters,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<RequestCancelCommand, Unit>
{
    public async Task<Unit> Handle(RequestCancelCommand request, CancellationToken ct)
    {
        var orderId = await BuyerOrders.OwnIdAsync(db, currentUser, request.Code, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(orderId, ct);
        if (order.Status != OrderStatus.ReadyToShip)
            throw new ConflictException(order.Status == OrderStatus.PendingConfirmation
                ? "Đơn chưa được shop xác nhận, bạn có thể huỷ trực tiếp."
                : $"Không thể yêu cầu huỷ đơn ở trạng thái \"{OrderStateMachine.Label(order.Status)}\".", "CANNOT_REQUEST");
        if (await db.OrderCancelRequests.AnyAsync(r => r.OrderId == order.Id && r.Status == CancelRequestStatus.Rejected, ct))
            throw new ConflictException("Shop đã từ chối yêu cầu huỷ của đơn này.", "ALREADY_REJECTED");
        var hours = await parameters.GetIntAsync(ParameterKeys.OrderCancelRequestHours, ct);
        db.OrderCancelRequests.Add(new OrderCancelRequest(order.Id, order.BuyerId, request.Reason, clock.UtcNow.AddHours(hours), clock.UtcNow));
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.CancelRequested, request.Reason.Trim()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

/// <summary>"Đã nhận được hàng": completes the order (opens the review window, starts settlement in later phases).</summary>
public record ConfirmReceivedCommand(string Code) : IRequest<Unit>;

public sealed class ConfirmReceivedHandler(IApplicationDbContext db, OrderLocks locks, IOutbox outbox, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ConfirmReceivedCommand, Unit>
{
    public async Task<Unit> Handle(ConfirmReceivedCommand request, CancellationToken ct)
    {
        var orderId = await BuyerOrders.OwnIdAsync(db, currentUser, request.Code, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(orderId, ct);
        OrderStateMachine.Transition(order, OrderStatus.Completed, OrderActor.Buyer, currentUser.UserId, "Người mua đã nhận được hàng", clock.UtcNow);
        outbox.Enqueue(OutboxTypes.OrderEvent, new OrderEventPayload(order.Id, OrderEvents.Completed, null));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

public record BuyAgainResultDto(int Added, IReadOnlyList<string> Skipped);

/// <summary>"Mua lại": every line still on sale goes back into the cart (quantity capped by what is available).</summary>
public record BuyAgainCommand(string Code) : IRequest<BuyAgainResultDto>;

public sealed class BuyAgainHandler(IApplicationDbContext db, CartStore carts, ISystemParameters parameters, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<BuyAgainCommand, BuyAgainResultDto>
{
    public async Task<BuyAgainResultDto> Handle(BuyAgainCommand request, CancellationToken ct)
    {
        var orderId = await BuyerOrders.OwnIdAsync(db, currentUser, request.Code, ct);
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId).OrderBy(i => i.Id).ToListAsync(ct);
        var skuIds = items.Select(i => i.SkuId).ToList();
        var sellable = await (from s in db.Skus
                              join p in db.Products on s.ProductId equals p.Id
                              where skuIds.Contains(s.Id) && s.IsActive && p.Status == ProductStatus.Active
                                    && db.Shops.Any(sh => sh.Id == p.ShopId && sh.Status == ShopStatus.Active)
                              select new { s.Id, s.Price, Available = s.Stock - s.Reserved }).ToDictionaryAsync(x => x.Id, ct);
        var cart = await carts.GetOrCreateAsync(new CartOwner(currentUser.UserId, null), ct);
        var maxLines = (int)await parameters.GetIntAsync(ParameterKeys.CartMaxLines, ct);
        var added = 0;
        var skipped = new List<string>();
        foreach (var item in items)
        {
            if (!sellable.TryGetValue(item.SkuId, out var s) || s.Available <= 0)
            {
                skipped.Add(item.NameSnapshot);
                continue;
            }
            var already = cart.Find(item.SkuId)?.Quantity ?? 0;
            var quantity = Math.Min(item.Quantity, s.Available - already);
            if (quantity <= 0)
            {
                skipped.Add(item.NameSnapshot);
                continue;
            }
            cart.Add(item.SkuId, quantity, await carts.UnitPriceAsync(item.SkuId, s.Price, ct), maxLines, clock.UtcNow);
            added++;
        }
        await db.SaveChangesAsync(ct);
        return new BuyAgainResultDto(added, skipped);
    }
}

// ---------- public tracking (no personal data) ----------

public record TrackingDto(string TrackingNo, string CarrierName, ShipmentStatus Status, string StatusLabel, DateTimeOffset ExpectedDeliveryAt,
    IReadOnlyList<ShipmentEventDto> Events);

public record TrackShipmentQuery(string TrackingNo) : IRequest<TrackingDto>;

public sealed class TrackShipmentHandler(IApplicationDbContext db) : IRequestHandler<TrackShipmentQuery, TrackingDto>
{
    public async Task<TrackingDto> Handle(TrackShipmentQuery request, CancellationToken ct)
    {
        var no = request.TrackingNo.Trim().ToUpperInvariant();
        var s = await db.Shipments.AsNoTracking().Include(x => x.Events).FirstOrDefaultAsync(x => x.TrackingNo == no, ct)
                ?? throw new NotFoundException("Không tìm thấy mã vận đơn.");
        var carrier = await db.Carriers.Where(c => c.Code == s.CarrierCode).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? s.CarrierCode;
        var dto = ShipmentDto.From(s, carrier);
        return new TrackingDto(s.TrackingNo, carrier, s.Status, dto.StatusLabel, s.ExpectedDeliveryAt, dto.Events);
    }
}

// ---------- notifications ----------

public record NotificationDto(Guid Id, NotificationCategory Category, string Title, string Body, string? Link, bool IsRead, DateTimeOffset CreatedAt);

public record ListNotificationsQuery(NotificationCategory? Category = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<NotificationDto>>, IPagedRequest;

public sealed class ListNotificationsValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsValidator() => this.ApplyPagingRules();
}

public sealed class ListNotificationsHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListNotificationsQuery, PagedResult<NotificationDto>>
{
    public async Task<PagedResult<NotificationDto>> Handle(ListNotificationsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (request.Category is { } c) q = q.Where(n => n.Category == c);
        return await q.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .ToPagedResultAsync(n => new NotificationDto(n.Id, n.Category, n.Title, n.Body, n.Link, n.IsRead, n.CreatedAt), request, ct);
    }
}

public record UnreadCountsDto(int Total, IReadOnlyDictionary<NotificationCategory, int> ByCategory);

public record UnreadNotificationsQuery : IRequest<UnreadCountsDto>;

public sealed class UnreadNotificationsHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<UnreadNotificationsQuery, UnreadCountsDto>
{
    public async Task<UnreadCountsDto> Handle(UnreadNotificationsQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var counts = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId && !n.IsRead)
            .GroupBy(n => n.Category).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new UnreadCountsDto(counts.Values.Sum(), counts);
    }
}

/// <summary>Mark one (<paramref name="Id"/>) or all of the caller's notifications as read.</summary>
public record MarkNotificationsReadCommand(Guid? Id) : IRequest<int>;

public sealed class MarkNotificationsReadHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<MarkNotificationsReadCommand, int>
{
    public async Task<int> Handle(MarkNotificationsReadCommand request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var q = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (request.Id is { } id)
        {
            if (!await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId, ct)) throw new NotFoundException("Không tìm thấy thông báo.");
            q = q.Where(n => n.Id == id);
        }
        var now = clock.UtcNow;
        return await q.ExecuteUpdateAsync(u => u.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now), ct);
    }
}
