using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Admin;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Outbox;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Notifications;

/// <summary>
/// Turns order events into in-app notifications for the buyer and for the shop's staff who handle orders (spec VII).
/// Runs after the business transaction committed; a redelivered message creates nothing twice (dedupe key).
/// </summary>
public sealed class OrderEventHandler(ShopHubDbContext db, MessageTemplates templates, IClock clock) : IOutboxHandler
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

        // Which side hears about which event; the words are the editable templates ORDER.{EVENT}.{BUYER|SHOP} (admin → Nội dung & mẫu tin)
        var placed = order.PaymentMethod == PaymentMethod.Cod ? "PLACED_COD" : OrderEvents.Placed;
        string? buyerEvent = e.Event switch
        {
            OrderEvents.Placed => placed,
            OrderEvents.Paid or OrderEvents.PaymentFailed or OrderEvents.Confirmed or OrderEvents.Shipped or OrderEvents.Delivered or OrderEvents.DeliveryFailed
                or OrderEvents.Completed or OrderEvents.Cancelled or OrderEvents.Returned or OrderEvents.CancelRejected or OrderEvents.Refunded
                or OrderEvents.ReturnUpdated or OrderEvents.ReturnRefunded or OrderEvents.DisputeDecided => e.Event,
            _ => null,
        };
        string? sellerEvent = e.Event switch
        {
            OrderEvents.Placed when order.PaymentMethod == PaymentMethod.Cod => placed,
            OrderEvents.Paid or OrderEvents.CancelRequested or OrderEvents.Cancelled or OrderEvents.Completed or OrderEvents.Returned
                or OrderEvents.ReturnRequested or OrderEvents.DisputeOpened or OrderEvents.DisputeDecided => e.Event,
            _ => null,
        };
        var values = new Dictionary<string, string>
        {
            ["code"] = order.Code,
            ["total"] = total,
            ["shop"] = shop.Name,
            ["note"] = e.Note ?? "",
            ["deadline"] = expires is { } x ? $" trước {VietnamTime.Format(x)}" : "",
        };
        async Task<Message?> RenderAsync(string? orderEvent, string side)
        {
            if (orderEvent is null) return null;
            var (title, body) = await templates.RenderAsync(TemplateCatalog.OrderKey(orderEvent, side), TemplateChannel.InApp, values, ct);
            var category = orderEvent is OrderEvents.Refunded or OrderEvents.ReturnRefunded && side == "BUYER"
                ? NotificationCategory.Wallet : NotificationCategory.Order;
            return new Message(title ?? "", body, category);
        }
        var buyer = await RenderAsync(buyerEvent, "BUYER");
        var seller = await RenderAsync(sellerEvent, "SHOP");

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
