using ShopHub.Domain.Common;

namespace ShopHub.Domain.Sales;

/// <summary>
/// The only place an order's status changes (spec 3.7). Every transition is checked against the table below and
/// recorded in <see cref="OrderStatusHistory"/>; an illegal one is a Vietnamese 409, never a 500.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.PendingConfirmation, OrderStatus.Cancelled],
        [OrderStatus.PendingConfirmation] = [OrderStatus.ReadyToShip, OrderStatus.Cancelled],
        [OrderStatus.ReadyToShip] = [OrderStatus.Shipping, OrderStatus.Cancelled],
        [OrderStatus.Shipping] = [OrderStatus.Delivered, OrderStatus.DeliveryFailed],
        [OrderStatus.DeliveryFailed] = [OrderStatus.Returning, OrderStatus.Shipping],
        [OrderStatus.Returning] = [OrderStatus.Returned],
        [OrderStatus.Delivered] = [OrderStatus.Completed],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Returned] = [],
    };

    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Chờ thanh toán",
        OrderStatus.PendingConfirmation => "Chờ xác nhận",
        OrderStatus.ReadyToShip => "Chờ lấy hàng",
        OrderStatus.Shipping => "Đang giao",
        OrderStatus.Delivered => "Đã giao",
        OrderStatus.Completed => "Hoàn thành",
        OrderStatus.Cancelled => "Đã huỷ",
        OrderStatus.DeliveryFailed => "Giao thất bại",
        OrderStatus.Returning => "Đang hoàn về",
        OrderStatus.Returned => "Đã hoàn về",
        _ => status.ToString(),
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>History row for the state an order is created in.</summary>
    public static OrderStatusHistory Start(Order order, OrderActor actor, Guid? actorId, DateTimeOffset now)
    {
        var entry = new OrderStatusHistory(order.Id, null, order.Status, actor, actorId, null, now);
        order.History.Add(entry);
        return entry;
    }

    public static OrderStatusHistory Transition(Order order, OrderStatus to, OrderActor actor, Guid? actorId, string? reason, DateTimeOffset now)
    {
        if (!CanTransition(order.Status, to))
            throw new BusinessRuleException($"Không thể chuyển đơn {order.Code} từ \"{Label(order.Status)}\" sang \"{Label(to)}\".");

        var from = order.Status;
        order.Status = to;
        switch (to)
        {
            case OrderStatus.PendingConfirmation when from == OrderStatus.PendingPayment:
                order.PaymentStatus = OrderPaymentStatus.Paid;
                order.PaidAt = now;
                break;
            case OrderStatus.ReadyToShip:
                order.ConfirmedAt = now;
                break;
            case OrderStatus.Shipping when from == OrderStatus.ReadyToShip:
                order.ShippedAt = now;
                break;
            case OrderStatus.Delivered:
                order.DeliveredAt = now;
                if (order.PaymentMethod == PaymentMethod.Cod)
                {
                    order.PaymentStatus = OrderPaymentStatus.Paid;
                    order.PaidAt = now;
                }
                break;
            case OrderStatus.Completed:
                order.CompletedAt = now;
                break;
            case OrderStatus.Cancelled:
                if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Huỷ đơn cần có lý do.");
                order.CancelledAt = now;
                order.CancelReason = reason;
                order.CancelledBy = actor;
                break;
        }

        var entry = new OrderStatusHistory(order.Id, from, to, actor, actorId, reason, now);
        order.History.Add(entry);
        return entry;
    }
}
