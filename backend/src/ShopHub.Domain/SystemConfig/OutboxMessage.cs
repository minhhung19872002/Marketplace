using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

// Side effect recorded in the same transaction as the business change, dispatched later by a background job
public class OutboxMessage : Entity
{
    private OutboxMessage() { }

    public OutboxMessage(string type, string payload, DateTimeOffset occurredAt)
    {
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public void MarkProcessed(DateTimeOffset at)
    {
        ProcessedAt = at;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
