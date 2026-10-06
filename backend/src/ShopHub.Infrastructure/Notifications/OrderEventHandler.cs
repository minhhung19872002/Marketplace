using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Notifications;

/// <summary>
/// Turns order events into in-app notifications for the buyer and for the shop's staff who handle orders (spec VII).
/// Runs after the business transaction committed; a redelivered message creates nothing twice (dedupe key).
/// </summary>
public sealed class OrderEventHandler(ShopHubDbContext db, IClock clock) : IOutboxHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public string Type => OutboxTypes.OrderEvent;

    private sealed record Message(string Title, string Body, NotificationCategory Category = NotificationCategory.Order);

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<OrderEventPayload>(payload, Json) ?? throw new InvalidOperationException("Tin outbox rỗng.");
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == e.OrderId, ct);
        if (order is null) return;
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == order.ShopId, ct);
        var total = $"₫{order.GrandTotal.ToString("N0", Vi)}";
        var expires = await db.CheckoutSessions.AsNoTracking().Where(c => c.Id == order.CheckoutId).Select(c => c.PaymentExpiresAt).FirstOrDefaultAsync(ct);

        Message? buyer = e.Event switch
        {
            OrderEvents.Placed when order.PaymentMethod == PaymentMethod.Cod =>
                new("Đặt hàng thành công", $"Đơn {order.Code} ({total}) đã được đặt, đang chờ {shop.Name} xác nhận."),
            OrderEvents.Placed => new("Đơn hàng chờ thanh toán",
                $"Vui lòng thanh toán đơn {order.Code} ({total}){(expires is { } x ? $" trước {VietnamTime.Format(x)}" : "")}, quá hạn đơn sẽ tự huỷ."),
            OrderEvents.Paid => new("Thanh toán thành công", $"Đã nhận thanh toán {total} cho đơn {order.Code}."),
            OrderEvents.Confirmed => new("Shop đã xác nhận đơn hàng", $"{shop.Name} đang chuẩn bị đơn {order.Code}. Mã vận đơn: {e.Note}."),
            OrderEvents.Shipped => new("Đơn hàng đang được giao", $"Đơn {order.Code} đã được giao cho đơn vị vận chuyển."),
            OrderEvents.Delivered => new("Giao hàng thành công",
                $"Đơn {order.Code} đã được giao. Vui lòng kiểm tra và bấm \"Đã nhận được hàng\"; đơn sẽ tự hoàn thành sau vài ngày."),
            OrderEvents.DeliveryFailed => new("Giao hàng không thành công", $"Đơn {order.Code}: {e.Note}. Đơn vị vận chuyển sẽ liên hệ giao lại."),
            OrderEvents.Completed => new("Đơn hàng đã hoàn thành", $"Cảm ơn bạn đã mua sắm tại {shop.Name}. Hãy đánh giá sản phẩm của đơn {order.Code}."),
            OrderEvents.Cancelled => new("Đơn hàng đã huỷ", $"Đơn {order.Code} đã huỷ. Lý do: {e.Note}."),
            OrderEvents.Returned => new("Đơn hàng đã hoàn về shop", $"Đơn {order.Code} không giao được và đã hoàn về {shop.Name}."),
            OrderEvents.CancelRejected => new("Shop từ chối yêu cầu huỷ", $"Đơn {order.Code} vẫn được giao. Lý do: {e.Note}."),
            OrderEvents.Refunded => new("Đã hoàn tiền", $"Đã hoàn {total} của đơn {order.Code} về phương thức thanh toán ban đầu.", NotificationCategory.Wallet),
            _ => null,
        };
        Message? seller = e.Event switch
        {
            OrderEvents.Placed when order.PaymentMethod == PaymentMethod.Cod => new("Đơn hàng mới", $"Đơn {order.Code} ({total}, COD) đang chờ xác nhận."),
            OrderEvents.Paid => new("Đơn hàng mới", $"Đơn {order.Code} ({total}) đã thanh toán, đang chờ xác nhận."),
            OrderEvents.CancelRequested => new("Yêu cầu huỷ đơn", $"Người mua muốn huỷ đơn {order.Code}. Lý do: {e.Note}. Vui lòng phản hồi trong 24 giờ."),
            OrderEvents.Cancelled => new("Đơn hàng đã huỷ", $"Đơn {order.Code} đã huỷ. Lý do: {e.Note}."),
            OrderEvents.Completed => new("Đơn hàng hoàn thành", $"Đơn {order.Code} ({total}) đã hoàn thành."),
            OrderEvents.Returned => new("Đơn hàng hoàn về", $"Đơn {order.Code} giao không thành công đã hoàn về kho."),
            _ => null,
        };

        var now = clock.UtcNow;
        // Stable across processes (string.GetHashCode is randomised): same event + note → same key
        var noteHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(e.Note ?? "")))[..8];
        var key = $"order:{order.Id}:{e.Event}:{noteHash}";
        if (buyer is not null)
            await AddAsync(new Notification(order.BuyerId, buyer.Category, buyer.Title, buyer.Body, $"/tai-khoan/don-mua/{order.Code}", "order", order.Id, now,
                $"{key}:b"), ct);
        if (seller is not null)
        {
            var staff = await db.ShopStaff.AsNoTracking().Where(s => s.ShopId == shop.Id).ToListAsync(ct);
            foreach (var member in staff.Where(s => s.Role == ShopStaffRole.Owner || s.Has(Application.Security.ShopPermissions.OrderView)))
                await AddAsync(new Notification(member.UserId, NotificationCategory.Order, seller.Title, seller.Body, $"/seller/don-hang?ma={order.Code}",
                    "order", order.Id, now, $"{key}:s"), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task AddAsync(Notification n, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(x => x.UserId == n.UserId && x.DedupeKey == n.DedupeKey, ct)) return;
        db.Notifications.Add(n);
    }
}
