namespace ShopHub.Application.SystemConfig;

// Outbox message type names — each must have a registered IOutboxHandler
public static class OutboxTypes
{
    public const string SystemParameterChanged = "sys.parameter.changed";
}

public record SystemParameterChangedPayload(string Key);
