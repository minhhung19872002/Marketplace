namespace ShopHub.Application.Abstractions;

/// <summary>Event names pushed to browsers over SignalR (spec 1: chat, notifications, order status).</summary>
public static class RealtimeEvents
{
    public const string ChatMessage = "chat.message";
    public const string ChatRead = "chat.read";
    public const string ChatTyping = "chat.typing";
    public const string Notification = "notification";
}

/// <summary>Pushes an event to every open connection of the given users (all their tabs and devices).</summary>
public interface IRealtime
{
    Task ToUsersAsync(IReadOnlyCollection<Guid> userIds, string eventName, object payload, CancellationToken ct);
}
