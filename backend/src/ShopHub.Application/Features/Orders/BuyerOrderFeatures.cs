using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Logistics;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Orders;

public record OrderItemDto(Guid Id, Guid SkuId, Guid ProductId, string Name, string? Variant, string? ImageUrl, long UnitPrice, long OriginalPrice,
    int Quantity, long LineTotal, long ShopDiscount, long PlatformDiscount, long CoinDiscount, long PaidAmount);

public record OrderSummaryDto(Guid Id, string Code, Guid ShopId, string ShopName, string ShopSlug, OrderStatus Status, string StatusLabel,
    OrderPaymentStatus PaymentStatus, PaymentMethod PaymentMethod, long GrandTotal, int ItemCount, OrderItemDto? FirstItem, DateTimeOffset CreatedAt,
    Guid CheckoutId);

public record OrderHistoryDto(OrderStatus? From, OrderStatus To, string ToLabel, OrderActor Actor, string? Reason, DateTimeOffset OccurredAt);

public record OrderAddressDto(string ReceiverName, string Phone, string FullAddress);

public record ShipmentEventDto(ShipmentStatus Status, string Label, string? Location, string Description, DateTimeOffset OccurredAt);

public record ShipmentDto(Guid Id, string TrackingNo, string CarrierCode, string? CarrierName, ShipmentStatus Status, string StatusLabel, PickupMethod PickupMethod,
    string? PickupSlot, long CodAmount, int WeightG, DateTimeOffset ExpectedDeliveryAt, IReadOnlyList<ShipmentEventDto> Events)
{
    public static ShipmentDto From(Shipment s, string? carrierName) => new(s.Id, s.TrackingNo, s.CarrierCode, carrierName, s.Status,
        Shipment.Label(s.Status), s.PickupMethod, s.PickupSlot, s.CodAmount, s.WeightG, s.ExpectedDeliveryAt,
        s.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new ShipmentEventDto(e.Status, Shipment.Label(e.Status), e.Location, e.Description, e.OccurredAt)).ToList());
}

public record CancelRequestDto(Guid Id, string Reason, CancelRequestStatus Status, DateTimeOffset CreatedAt, DateTimeOffset DueAt, string? RejectReason);

/// <summary>What the buyer may do now (drives the buttons of "Đơn mua").</summary>
public record BuyerOrderActionsDto(bool Pay, bool Cancel, bool RequestCancel, bool ConfirmReceived, bool BuyAgain, bool Review, bool Return);

public record OrderDetailDto(
    Guid Id,
    string Code,
    Guid CheckoutId,
    Guid ShopId,
    string ShopName,
    string ShopSlug,
    OrderStatus Status,
    string StatusLabel,
    OrderPaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    string CarrierCode,
    string? CarrierName,
    int ExpectedDeliveryDays,
    OrderAddressDto Address,
    string? BuyerNote,
    IReadOnlyList<OrderItemDto> Items,
    long Subtotal,
    long ShopDiscount,
    long PlatformDiscount,
    long ShippingFee,
    long ShippingDiscount,
    long CoinUsed,
    long GrandTotal,
    string? CancelReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaymentExpiresAt,
    IReadOnlyList<OrderHistoryDto> History,
    ShipmentDto? Shipment,
    CancelRequestDto? CancelRequest,
    BuyerOrderActionsDto Actions,
    DateTimeOffset? AutoCompleteAt);

/// <summary>Buyer's order tabs (spec II.8). Status groups map to the tabs; other buyers' orders never show (404).</summary>
public enum BuyerOrderTab
{
    All,
    AwaitingPayment,
    Processing,  // chờ xác nhận + chờ lấy hàng
    Shipping,
    Completed,   // đã giao + hoàn thành
    Cancelled,
    Returns,
}

public record ListMyOrdersQuery(BuyerOrderTab Tab = BuyerOrderTab.All, string? Q = null, int Page = 1, int PageSize = 10)
    : IRequest<PagedResult<OrderSummaryDto>>, IPagedRequest;

public sealed class ListMyOrdersValidator : AbstractValidator<ListMyOrdersQuery>
{
    public ListMyOrdersValidator()
    {
        this.ApplyPagingRules();
        RuleFor(x => x.Q).MaximumLength(100).WithMessage("Từ khoá tối đa 100 ký tự.");
    }
}

internal static class OrderProjections
{
    public static OrderItemDto Item(OrderItem i) => new(i.Id, i.SkuId, i.ProductId, i.NameSnapshot, i.VariantSnapshot, i.ImageSnapshot, i.UnitPrice,
        i.OriginalPrice, i.Quantity, i.LineTotal,
        i.Discounts.Where(d => d.Source == DiscountSource.Shop).Sum(d => d.Amount),
        i.Discounts.Where(d => d.Source == DiscountSource.Platform).Sum(d => d.Amount),
        i.Discounts.Where(d => d.Source == DiscountSource.Coin).Sum(d => d.Amount),
        i.LineTotal - i.Discounts.Sum(d => d.Amount));
}

public sealed class ListMyOrdersHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<ListMyOrdersQuery, PagedResult<OrderSummaryDto>>
{
    public async Task<PagedResult<OrderSummaryDto>> Handle(ListMyOrdersQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var q = db.Orders.AsNoTracking().Where(o => o.BuyerId == userId);
        q = request.Tab switch
        {
            BuyerOrderTab.AwaitingPayment => q.Where(o => o.Status == OrderStatus.PendingPayment),
            BuyerOrderTab.Processing => q.Where(o => o.Status == OrderStatus.PendingConfirmation || o.Status == OrderStatus.ReadyToShip),
            BuyerOrderTab.Shipping => q.Where(o => o.Status == OrderStatus.Shipping || o.Status == OrderStatus.DeliveryFailed),
            BuyerOrderTab.Completed => q.Where(o => o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Completed),
            BuyerOrderTab.Cancelled => q.Where(o => o.Status == OrderStatus.Cancelled),
            BuyerOrderTab.Returns => q.Where(o => o.Status == OrderStatus.Returning || o.Status == OrderStatus.Returned
                                                  || db.ReturnRequests.Any(r => r.OrderId == o.Id)),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim();
            q = q.Where(o => o.Code == term.ToUpper()
                             || db.Shops.Any(s => s.Id == o.ShopId && s.Name.ToLower().Contains(term.ToLower()))
                             || o.Items.Any(i => i.NameSnapshot.ToLower().Contains(term.ToLower())));
        }

        var page = await q.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).ToPagedResultAsync(request, ct);
        var ids = page.Items.Select(o => o.Id).ToList();
        var shopIds = page.Items.Select(o => o.ShopId).Distinct().ToList();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var items = await db.OrderItems.AsNoTracking().Include(i => i.Discounts).Where(i => ids.Contains(i.OrderId)).ToListAsync(ct);
        return new PagedResult<OrderSummaryDto>(page.Items.Select(o =>
        {
            var mine = items.Where(i => i.OrderId == o.Id).OrderBy(i => i.Id).ToList();
            var shop = shops[o.ShopId];
            return new OrderSummaryDto(o.Id, o.Code, o.ShopId, shop.Name, shop.Slug, o.Status, OrderStateMachine.Label(o.Status), o.PaymentStatus,
                o.PaymentMethod, o.GrandTotal, mine.Sum(i => i.Quantity), mine.Select(OrderProjections.Item).FirstOrDefault(), o.CreatedAt, o.CheckoutId);
        }).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}

public record GetMyOrderQuery(string Code) : IRequest<OrderDetailDto>;

public sealed class GetMyOrderHandler(IApplicationDbContext db, ISystemParameters parameters, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<GetMyOrderQuery, OrderDetailDto>
{
    public async Task<OrderDetailDto> Handle(GetMyOrderQuery request, CancellationToken ct)
    {
        var userId = UserGuard.Require(currentUser);
        var code = request.Code.Trim().ToUpperInvariant();
        // Owner filter in the query itself: someone else's order is "not found"
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Discounts).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Code == code && o.BuyerId == userId, ct) ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        return await OrderDetails.BuildAsync(db, parameters, clock, order, ct);
    }
}

internal static class OrderDetails
{
    public static async Task<OrderDetailDto> BuildAsync(IApplicationDbContext db, ISystemParameters parameters, IClock clock, Order order, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var reviewDeadline = order.CompletedAt?.AddDays(await parameters.GetIntAsync(ParameterKeys.ReviewWindowDays, ct));
        var returnDeadline = order.DeliveredAt?.AddDays(await parameters.GetIntAsync(ParameterKeys.ReturnWindowDays, ct));
        var lineIds = order.Items.Select(i => i.Id).ToList();
        var reviewed = await db.Reviews.CountAsync(r => lineIds.Contains(r.OrderItemId), ct);
        var openReturns = await db.ReturnItems.AnyAsync(i => lineIds.Contains(i.OrderItemId) && i.IsOpen, ct);
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == order.ShopId, ct);
        var checkout = await db.CheckoutSessions.AsNoTracking().SingleAsync(c => c.Id == order.CheckoutId, ct);
        var carrier = await db.Carriers.AsNoTracking().Where(c => c.Code == order.CarrierCode).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var address = JsonSerializer.Deserialize<OrderAddressDto>(checkout.AddressSnapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                      ?? new OrderAddressDto("", "", "");
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Events)
            .Where(s => s.OrderId == order.Id && s.Direction == ShipmentDirection.Outbound && s.Status != ShipmentStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        var cancel = await db.OrderCancelRequests.AsNoTracking().Where(r => r.OrderId == order.Id).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var pendingRequest = cancel?.Status == CancelRequestStatus.Pending;
        var actions = new BuyerOrderActionsDto(
            Pay: order.Status == OrderStatus.PendingPayment,
            Cancel: order.Status is OrderStatus.PendingPayment or OrderStatus.PendingConfirmation,
            RequestCancel: order.Status == OrderStatus.ReadyToShip && !pendingRequest && cancel?.Status != CancelRequestStatus.Rejected,
            ConfirmReceived: order.Status == OrderStatus.Delivered,
            BuyAgain: order.Status is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Returned,
            Review: order.Status == OrderStatus.Completed && reviewDeadline > now && reviewed < order.Items.Count,
            Return: order.Status is OrderStatus.Delivered or OrderStatus.Completed && returnDeadline > now && !openReturns);
        return new OrderDetailDto(order.Id, order.Code, order.CheckoutId, shop.Id, shop.Name, shop.Slug, order.Status, OrderStateMachine.Label(order.Status),
            order.PaymentStatus, order.PaymentMethod, order.CarrierCode, carrier, order.ExpectedDeliveryDays, address, order.BuyerNote,
            order.Items.OrderBy(i => i.Id).Select(OrderProjections.Item).ToList(),
            order.Subtotal, order.ShopDiscount, order.PlatformDiscount, order.ShippingFee, order.ShippingDiscount, order.CoinUsed, order.GrandTotal,
            order.CancelReason, order.CreatedAt, checkout.Status == CheckoutStatus.AwaitingPayment ? checkout.PaymentExpiresAt : null,
            order.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => new OrderHistoryDto(h.FromStatus, h.ToStatus, OrderStateMachine.Label(h.ToStatus), h.ActorType, h.Reason, h.OccurredAt)).ToList(),
            shipment is null ? null : ShipmentDto.From(shipment, carrier),
            cancel is null ? null : new CancelRequestDto(cancel.Id, cancel.Reason, cancel.Status, cancel.CreatedAt, cancel.DueAt, cancel.RejectReason),
            actions, order.AutoCompleteAt);
    }
}
