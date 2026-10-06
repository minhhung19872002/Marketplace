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
}

public record SystemParameterChangedPayload(string Key);

public record SmsPayload(string To, string Text);

public record EmailPayload(string To, string Subject, string Html);

public record SessionsChangedPayload(Guid UserId, Guid? SessionId);

public record ShopEventPayload(Guid ShopId, string Event, string? Reason);

public record ProductEventPayload(Guid ProductId, string Event, string? Reason);
