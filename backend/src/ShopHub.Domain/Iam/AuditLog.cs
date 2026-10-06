using ShopHub.Domain.Common;

namespace ShopHub.Domain.Iam;

// Automatic change journal written by the EF interceptor (who changed what, old/new values)
public class AuditLog : Entity
{
    private AuditLog() { }

    public AuditLog(Guid? userId, string? ip, string? userAgent, string action, string entity, string? entityId,
        string? oldValue, string? newValue, DateTimeOffset occurredAt)
    {
        UserId = userId;
        Ip = ip;
        UserAgent = userAgent;
        Action = action;
        Entity = entity;
        EntityId = entityId;
        OldValue = oldValue;
        NewValue = newValue;
        OccurredAt = occurredAt;
    }

    public Guid? UserId { get; private set; }
    public string? Ip { get; private set; }
    public string? UserAgent { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Entity { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
}
