namespace ShopHub.Application.SystemConfig;

// Outbox message type names — each must have a registered IOutboxHandler
public static class OutboxTypes
{
    public const string SystemParameterChanged = "sys.parameter.changed";
    public const string NotifySms = "notify.sms";
    public const string NotifyEmail = "notify.email";

    // Fan-out so every API instance drops cached session state (lock, logout, password change)
    public const string SessionsChanged = "iam.sessions.changed";
}

public record SystemParameterChangedPayload(string Key);

public record SmsPayload(string To, string Text);

public record EmailPayload(string To, string Subject, string Html);

public record SessionsChangedPayload(Guid UserId, Guid? SessionId);
