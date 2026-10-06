using ShopHub.Domain.Common;

namespace ShopHub.Domain.Engage;

/// <summary>The four tabs of /thong-bao.</summary>
public enum NotificationCategory
{
    Order,      // Cập nhật đơn hàng
    Promotion,  // Khuyến mãi
    Wallet,     // Ví & xu
    Activity,   // Hoạt động (shop, tài khoản…)
}

/// <summary>In-app notification, created by outbox handlers after the business transaction committed.</summary>
public class Notification : Entity
{
    private Notification() { }

    public Notification(Guid userId, NotificationCategory category, string title, string body, string? link, string? refType, Guid? refId,
        DateTimeOffset now, string? dedupeKey = null)
    {
        DedupeKey = dedupeKey;
        UserId = userId;
        Category = category;
        Title = title;
        Body = body;
        Link = link;
        RefType = refType;
        RefId = refId;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    // Where a click goes (an order, a voucher, a product…)
    public string? Link { get; private set; }
    public string? RefType { get; private set; }
    public Guid? RefId { get; private set; }
    // The same event delivered twice by the outbox creates one notification (unique per user)
    public string? DedupeKey { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = now;
    }
}
