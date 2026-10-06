namespace ShopHub.Application.SystemConfig;

// Outbox message type names — each must have a registered IOutboxHandler
public static class OutboxTypes
{
    public const string SystemParameterChanged = "sys.parameter.changed";
    public const string NotifySms = "notify.sms";
    public const string NotifyEmail = "notify.email";

    // Fan-out so every API instance drops cached session state (lock, logout, password change)
    public const string SessionsChanged = "iam.sessions.changed";

    // Shop application lifecycle: SUBMITTED / APPROVED / REJECTED / LOCKED → notify the owner
    public const string ShopEvent = "shop.event";

    // Product moderation outcome: APPROVED / REJECTED / BANNED → notify the shop owner
    public const string ProductEvent = "catalog.product.event";

    // Push product / shop changes into the search index (after commit, retried by the outbox)
    public const string SearchSyncProducts = "search.sync.products";
    public const string SearchSyncShop = "search.sync.shop";
    // A SKU's price in force changed (programme added / stopped / started / ended, flash quota used up): its product's document
    public const string SearchSyncSkus = "search.sync.skus";

    // Order lifecycle (placed, paid, confirmed, shipped, delivered, completed, cancelled, cancel request…) → notifications
    public const string OrderEvent = "sales.order.event";

    // A new in-app notification → realtime push + the email / SMS / push channels the user chose
    public const string NotifyDeliver = "notify.deliver";
}

public record NotifyDeliverPayload(Guid NotificationId);

public record SystemParameterChangedPayload(string Key);

public record SmsPayload(string To, string Text);

public record EmailPayload(string To, string Subject, string Html);

public record SessionsChangedPayload(Guid UserId, Guid? SessionId);

public record ShopEventPayload(Guid ShopId, string Event, string? Reason);

public record ProductEventPayload(Guid ProductId, string Event, string? Reason);

public record SearchSyncProductsPayload(IReadOnlyList<Guid> ProductIds);

public record SearchSyncSkusPayload(IReadOnlyList<Guid> SkuIds);

public record SearchSyncShopPayload(Guid ShopId);

public record OrderEventPayload(Guid OrderId, string Event, string? Note);

public static class OrderEvents
{
    public const string Placed = "PLACED";
    public const string Paid = "PAID";
    public const string Confirmed = "CONFIRMED";
    public const string Shipped = "SHIPPED";
    public const string Delivered = "DELIVERED";
    public const string DeliveryFailed = "DELIVERY_FAILED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string Returned = "RETURNED";
    public const string CancelRequested = "CANCEL_REQUESTED";
    public const string CancelRejected = "CANCEL_REJECTED";
    public const string Refunded = "REFUNDED";
    public const string ReturnRequested = "RETURN_REQUESTED";
    public const string ReturnUpdated = "RETURN_UPDATED";
    public const string ReturnRefunded = "RETURN_REFUNDED";
    public const string DisputeOpened = "DISPUTE_OPENED";
    public const string DisputeDecided = "DISPUTE_DECIDED";
}
